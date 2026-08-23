using System;
using System.Diagnostics;
using LabApi.Features.Audio;
using LabApi.Features.Wrappers;
using PlayerRoles.PlayableScps.Scp096;
using UnityEngine;

namespace Scp096ChaseMusic
{
    // Drives the chase music for a single SCP-096 player.
    //
    // The game only ever plays chase themes for human clients, so SCP-096 hears nothing by default. This works
    // around that by streaming the track to them the same way a speaker toy would: a non-spatial speaker is
    // spawned, and the audio frames for its controller id are transmitted to that one player only. Everyone
    // else keeps hearing the real, client-side chase theme.
    //
    // Sections are queued rather than crossfaded, which is what makes the enrage transition land: the build-up
    // and the enraged loop are handed to the transmitter together, so the loop starts on the exact sample the
    // build-up ends on.
    public sealed class ChaseMusicSession
    {
        private const float SpeakerMaxDistance = 10000f;

        // Speaker volume is a plain sample multiplier client side, so it can usefully go above 1.
        private const float MaxVolume = 5f;

        private readonly ReferenceHub _hub;
        private readonly string _nickname;
        private readonly ChaseMusicLibrary _library;
        private readonly ChaseMusicLibrary _bassLibrary;

        // Which mix this session is currently playing. Resolved when a section starts rather than every tick,
        // so toggling Bass mid-section cannot desync the section length from the samples actually playing.
        private ChaseMusicLibrary _active;
        private readonly Scp096ChaseMusicConfig _config;
        private readonly Stopwatch _sectionTimer = new Stopwatch();

        private SpeakerToy _speaker;
        private double _sectionLength;
        private float _appliedVolume = -1f;

        // 0 while silent, 1 at full volume. Only the edges of a chase are faded: section handovers stay hard
        // cuts, because the build-up rolling into the loop is meant to land on the beat, not smear across it.
        private float _fade;
        private float _fadeTarget;
        private ChaseSection? _pendingStop;

        public byte ControllerId { get; }

        // The section currently on air, or null when SCP-096 hears nothing.
        public ChaseSection? Playing { get; private set; }

        public event Action<ChaseSection?> SectionChanged;

        // True while this session still has a live SCP-096 behind it.
        //
        // The Unity == overload is the only safe thing to do with a destroyed hub: anything that hashes it,
        // including using it as a dictionary key, dereferences its gameObject and throws.
        public bool IsStillScp096 => _hub != null && _hub.roleManager.CurrentRole is Scp096Role;

        public ChaseMusicSession(ReferenceHub hub, string nickname, byte controllerId, ChaseMusicLibrary library,
            ChaseMusicLibrary bassLibrary, Scp096ChaseMusicConfig config)
        {
            _hub = hub;
            // Captured up front: reading it back off a destroyed hub would throw.
            _nickname = nickname;
            ControllerId = controllerId;
            _library = library;
            _bassLibrary = bassLibrary;
            _active = library;
            _config = config;
        }

        private AudioTransmitter Transmitter => SpeakerToy.GetTransmitter(ControllerId);

        public void Update(Scp096RageState rage, int targetCount)
        {
            if (!EnsureSpeaker())
                return;

            StartTailFadeIfEnding();
            AdvanceFade();
            ApplyVolume();

            // A fade-out that has run its course is the moment the stream actually stops.
            if (_fadeTarget <= 0f && _fade <= 0f && _pendingStop.HasValue)
            {
                Transmitter.Stop();
                _pendingStop = null;
                SetPlaying(null, 0);
            }

            ChaseSection? desired = Desired(rage, targetCount);

            // The calming section is a tail: the rage state only lasts 5s but the music runs longer, so let it
            // finish rather than cutting to silence or restarting the docile loop underneath it.
            if (Playing == ChaseSection.Calming && _config.PlayCalmingTailInFull && !TailFinished() &&
                (desired == null || desired == ChaseSection.Docile))
                return;

            // The enraged loop is already queued behind the build-up and will start by itself, sample accurate.
            if (Playing == ChaseSection.BuildUp && desired == ChaseSection.Enraged)
            {
                SetPlaying(ChaseSection.Enraged, double.PositiveInfinity);
                return;
            }

            if (desired == Playing && !NeedsRestart())
                return;

            Apply(desired);
        }


        private float ResolveVolume() =>
            Mathf.Clamp(ChaseMusicSettings.ResolveVolume(_hub, (float)_config.Volume), 0f, MaxVolume) * Shape(_fade);

        // Eases the raw 0-1 ramp into an S-curve.
        //
        // A straight amplitude ramp is not perceptually even - it leaves an audible kink where the fade starts
        // and where it lands. Smoothstep has zero slope at both ends, so the music drifts away instead.
        private static float Shape(float position) => position * position * (3f - 2f * position);

        // Starts fading a one-shot section that is about to run out of samples.
        //
        // The sections are cut from one continuous recording, so the calm-down does not resolve into silence -
        // it stops dead on the boundary where the next section begins. Waiting until the clip has finished to
        // fade is too late; the ramp has to close exactly as the samples run out.
        private void StartTailFadeIfEnding()
        {
            if (Playing != ChaseSection.Calming || double.IsInfinity(_sectionLength))
                return;

            double remaining = _sectionLength - _sectionTimer.Elapsed.TotalSeconds;
            if (remaining <= (double)_config.FadeOutSeconds)
                _fadeTarget = 0f;
        }

        // Moves the fade envelope towards its target at the configured rate.
        //
        // Driven off the tick rather than wall time so it stays in step with everything else in this class, and
        // so a paused or slow server fades slower rather than jumping.
        private void AdvanceFade()
        {
            float seconds = _fadeTarget > _fade
                ? (float)_config.FadeInSeconds
                : (float)_config.FadeOutSeconds;

            if (seconds <= 0f)
            {
                _fade = _fadeTarget;
                return;
            }

            float step = (float)_config.TickInterval / seconds;
            _fade = Mathf.MoveTowards(_fade, _fadeTarget, step);
        }

        // Pushes the listener's current slider position to their speaker.
        //
        // Volume is a SyncVar, so it is only written when it actually changes - assigning every tick would put
        // a network message on the wire twenty times a second for nothing.
        private void ApplyVolume()
        {
            float wanted = ResolveVolume();
            if (Mathf.Abs(wanted - _appliedVolume) < 0.001f)
                return;

            _speaker.Volume = wanted;
            _appliedVolume = wanted;
        }

        // Fades the music down for a session whose SCP-096 is gone, and reports when it is safe to dispose.
        //
        // Dying or a role change would otherwise cut the music dead mid-bar. The session is kept alive a moment
        // longer so the sound can leave the way it arrived.
        public bool FadeOutAndCheckFinished()
        {
            if (_speaker == null || _speaker.IsDestroyed || !Playing.HasValue)
                return true;

            _fadeTarget = 0f;
            AdvanceFade();
            ApplyVolume();

            if (_fade > 0f)
                return false;

            Transmitter.Stop();
            return true;
        }

        public void Dispose()
        {
            try
            {
                Transmitter.Stop();
                Transmitter.ValidPlayers = _ => false;
            }
            catch (Exception)
            {
                // A transmitter that was never started has nothing to stop.
            }

            if (_speaker != null && !_speaker.IsDestroyed)
                _speaker.Destroy();

            _speaker = null;
            Playing = null;
        }

        private ChaseSection? Desired(Scp096RageState rage, int targetCount)
        {
            switch (rage)
            {
                case Scp096RageState.Distressed:
                    return ChaseSection.BuildUp;
                case Scp096RageState.Enraged:
                    return ChaseSection.Enraged;
                case Scp096RageState.Calming:
                    return ChaseSection.Calming;
                default:
                    return targetCount > 0 || !_config.DocileRequiresTargets ? ChaseSection.Docile : (ChaseSection?)null;
            }
        }

        private void Apply(ChaseSection? desired)
        {
            AudioTransmitter transmitter = Transmitter;

            if (desired.HasValue)
            {
                // Coming from silence, ease in; already playing, stay where the fade is so a section change
                // does not duck the volume.
                if (!Playing.HasValue)
                    _fade = 0f;

                _fadeTarget = 1f;
                _pendingStop = null;

                // Pick the mix here, so a section always plays the samples its length was measured from.
                _active = _bassLibrary != null && ChaseMusicSettings.WantsBass(_hub) ? _bassLibrary : _library;
            }

            switch (desired)
            {
                case null:
                    // Ask for silence rather than taking it: the stream keeps running until the fade lands.
                    if (Playing.HasValue)
                    {
                        _pendingStop = Playing;
                        _fadeTarget = 0f;
                    }

                    break;

                case ChaseSection.Docile:
                    transmitter.Play(_active[ChaseSection.Docile], queue: false, loop: true);
                    SetPlaying(ChaseSection.Docile, double.PositiveInfinity);
                    break;

                case ChaseSection.BuildUp:
                    // Both clips go in at once so the loop picks up exactly where the build-up drops off.
                    transmitter.Play(_active[ChaseSection.BuildUp], queue: false, loop: false);
                    transmitter.Play(_active[ChaseSection.Enraged], queue: true, loop: true);
                    SetPlaying(ChaseSection.BuildUp, _active.DurationOf(ChaseSection.BuildUp));
                    break;

                case ChaseSection.Enraged:
                    transmitter.Play(_active[ChaseSection.Enraged], queue: false, loop: true);
                    SetPlaying(ChaseSection.Enraged, double.PositiveInfinity);
                    break;

                case ChaseSection.Calming:
                    transmitter.Play(_active[ChaseSection.Calming], queue: false, loop: false);
                    SetPlaying(ChaseSection.Calming, _active.DurationOf(ChaseSection.Calming));
                    break;
            }
        }

        private void SetPlaying(ChaseSection? section, double length)
        {
            bool changed = Playing != section;
            Playing = section;
            _sectionLength = length;
            _sectionTimer.Restart();

            if (changed)
                SectionChanged?.Invoke(section);
        }

        private bool TailFinished() => _sectionTimer.Elapsed.TotalSeconds >= _sectionLength;

        // True when a looping section should be playing but the transmitter has stopped, e.g. because its
        // coroutine hit an error. Without this the music would stay dead until the next state change.
        private bool NeedsRestart()
        {
            if (Playing != ChaseSection.Docile && Playing != ChaseSection.Enraged)
                return false;

            // Give the transmitter a moment to spin its coroutine up before deciding it has died.
            return _sectionTimer.Elapsed.TotalSeconds > 1 && !Transmitter.IsPlaying;
        }

        // Spawns the speaker, or respawns it if the round restart took it with it.
        private bool EnsureSpeaker()
        {
            if (_speaker != null && !_speaker.IsDestroyed)
                return true;

            // Losing the speaker (a round restart despawns it) leaves the transmitter streaming into nothing.
            if (_speaker != null)
                Transmitter.Stop();

            try
            {
                _speaker = SpeakerToy.Create(Vector3.zero, null, networkSpawn: false);
                _speaker.ControllerId = ControllerId;
                _speaker.IsSpatial = false;
                _speaker.Volume = ResolveVolume();
                _appliedVolume = _speaker.Volume;
                _speaker.MinDistance = 1f;

                // Clients cull speaker playback beyond MaxDistance even when it is non-spatial, so keep the
                // radius large enough that SCP-096 can never walk out of their own soundtrack.
                _speaker.MaxDistance = SpeakerMaxDistance;
                _speaker.Spawn();
            }
            catch (Exception ex)
            {
                LabApi.Features.Console.Logger.Error("[Scp096ChaseMusic] Failed to spawn the speaker for " +
                                                     _nickname + ": " + ex);
                _speaker = null;
                return false;
            }

            ReferenceHub listener = _hub;
            Transmitter.ValidPlayers = player => player != null && player.ReferenceHub == listener;

            // A fresh speaker means a fresh stream; whatever we thought was playing is gone.
            Playing = null;
            _sectionLength = 0;
            _sectionTimer.Reset();
            return true;
        }
    }
}
