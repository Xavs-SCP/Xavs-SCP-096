using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabApi.Features.Console;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;
using LabApi.Features.Wrappers;
using MEC;
using PlayerRoles.PlayableScps.Scp096;

namespace Scp096ChaseMusic
{
    // Gives the SCP-096 player the chase music that the game only ever plays for humans.
    //
    // The client decides who hears a chase theme, and it force-disables every theme unless the local player is
    // human (ChaseThemeSoundtrack.UpdateVolume). That is not something a server plugin can talk it out of, so
    // instead the track is streamed to SCP-096 over the speaker toy audio path, with the sections picked to
    // match the rage state the rest of the server is already reacting to.
    public class Scp096ChaseMusicPlugin : Plugin<Scp096ChaseMusicConfig>
    {
        // LabAPI's logger already tags every line with the plugin name, so this adds nothing but noise.
        private const string LogPrefix = "";

        // Keyed by network id, never by ReferenceHub. ReferenceHub.GetHashCode() dereferences its gameObject,
        // so once a player is destroyed the hub cannot be hashed at all - looking one up, or even removing it,
        // throws. Round restarts destroy every hub at once, which made the whole dictionary untouchable.
        private readonly Dictionary<uint, ChaseMusicSession> _sessions =
            new Dictionary<uint, ChaseMusicSession>();

        private CoroutineHandle _tick;
        private ChaseMusicLibrary _library;
        private ChaseMusicLibrary _bassLibrary;
        private float _tickInterval;

        public override string Name => "Scp096ChaseMusic";

        public override string Description => "Lets the player controlling SCP-096 hear their own chase music.";

        public override string Author => "xavie";

        public override Version Version => new Version(1, 0, 0);

        public override Version RequiredApiVersion => new Version(1, 1, 0);

        public override void Enable()
        {
            if (!Config.IsEnabled)
            {
                Logger.Info(LogPrefix + "Disabled in config.");
                return;
            }

            if (!TryParseVariant(Config.Variant, out ChaseVariant variant))
            {
                Logger.Error(LogPrefix + Config.Variant + " is not a valid variant. Use NonTarget or Target.");
                return;
            }

            string audioPath = ResolveAudioPath(out List<string> searched);
            if (audioPath == null)
            {
                Logger.Error(LogPrefix + "Cannot find " + Config.AudioFile + ". Looked in:");
                foreach (string candidate in searched)
                    Logger.Error(LogPrefix + "  " + candidate);

                return;
            }

            _library = ChaseMusicLibrary.TryLoad(audioPath, variant, Config.Timeline, out string error);
            if (_library == null)
            {
                Logger.Error(LogPrefix + "Could not read the audio: " + error);
                return;
            }

            _tickInterval = Config.TickInterval > 0 ? (float)Config.TickInterval : 0.05f;

            // Optional second mix. Missing or unreadable simply means the tick box is not offered.
            _bassLibrary = LoadOptionalBass(variant);

            if (Config.ShowVolumeSlider)
            {
                ChaseMusicSettings.Register(Config.VolumeSliderId, Config.VolumeSliderDefaultPercent,
                    _bassLibrary != null, Config.BassToggleId, Config.BassOnByDefault);

                Logger.Info(LogPrefix + "Player settings registered: volume" +
                            (_bassLibrary != null ? " and Bass." : "."));
            }

            Logger.Info(LogPrefix + "Loaded " + Path.GetFileName(audioPath) + " (" + variant + " mix): " + _library.Describe() + ".");
            _tick = Timing.RunCoroutine(TickCoroutine(), Segment.Update);
        }

        public override void Disable()
        {
            Timing.KillCoroutines(_tick);
            ChaseMusicSettings.Unregister();

            foreach (ChaseMusicSession session in _sessions.Values)
                session.Dispose();

            _sessions.Clear();
            _library = null;
            _bassLibrary = null;
        }

        private IEnumerator<float> TickCoroutine()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(_tickInterval);

                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    Logger.Error(LogPrefix + "Tick failed: " + ex);
                }
            }
        }

        // Polls every live SCP-096 and reconciles their music with their rage state.
        //
        // Polling rather than hooking the state events keeps this immune to role pooling, disconnects and round
        // restarts: a session that no longer has an SCP-096 behind it simply stops existing on the next tick.
        private void Poll()
        {
            foreach (uint networkId in _sessions.Keys.ToArray())
            {
                ChaseMusicSession session = _sessions[networkId];
                if (session.IsStillScp096)
                    continue;

                // Let it fade before tearing it down, so death or a role change does not cut the music dead.
                if (session.FadeOutAndCheckFinished())
                    EndSession(networkId);
            }

            foreach (Player player in Player.List)
            {
                // Hub first: RoleBase reads through it, and touching a destroyed hub throws rather than
                // returning null.
                ReferenceHub hub = player.ReferenceHub;
                if (hub == null)
                    continue;

                if (!(player.RoleBase is Scp096Role role))
                    continue;

                if (!_sessions.TryGetValue(player.NetworkId, out ChaseMusicSession session))
                {
                    if (!TryAllocateControllerId(out byte controllerId))
                    {
                        Logger.Warn(LogPrefix + "Out of speaker ids, no music for " + player.Nickname + ".");
                        continue;
                    }

                    session = new ChaseMusicSession(hub, player.Nickname, controllerId, _library, _bassLibrary, Config);
                    if (Config.VerboseLogging)
                    {
                        string nickname = player.Nickname;
                        session.SectionChanged += section =>
                            Logger.Debug(LogPrefix + nickname + " -> " + (section.HasValue ? section.Value.ToString() : "silence"));
                    }

                    _sessions[player.NetworkId] = session;
                }

                session.Update(role.StateController.RageState, CountTargets(role));
            }
        }

        // Loads the bass mix if one is configured and present. Its absence is not an error: the tick box is
        // simply not offered.
        private ChaseMusicLibrary LoadOptionalBass(ChaseVariant variant)
        {
            if (string.IsNullOrWhiteSpace(Config.BassAudioFile))
                return null;

            string path = ResolveAudioPath(Config.BassAudioFile, out _);
            if (path == null)
            {
                Logger.Info(LogPrefix + "No bass mix at " + Config.BassAudioFile + ", hiding the Bass option.");
                return null;
            }

            ChaseMusicLibrary library = ChaseMusicLibrary.TryLoad(path, variant, Config.Timeline, out string error);
            if (library == null)
            {
                Logger.Warn(LogPrefix + "Could not load the bass mix: " + error);
                return null;
            }

            Logger.Info(LogPrefix + "Loaded bass mix " + Path.GetFileName(path) + ": " + library.Describe() + ".");
            return library;
        }

        private static int CountTargets(Scp096Role role)
        {
            return role.SubroutineModule.TryGetSubroutine(out Scp096TargetsTracker tracker) ? tracker.Targets.Count : 0;
        }

        private void EndSession(uint networkId)
        {
            if (_sessions.TryGetValue(networkId, out ChaseMusicSession session))
            {
                session.Dispose();
                _sessions.Remove(networkId);
            }
        }

        // Finds the lowest controller id at or above the configured base that no session is using.
        private bool TryAllocateControllerId(out byte controllerId)
        {
            var taken = new HashSet<byte>(_sessions.Values.Select(s => s.ControllerId));

            for (int id = Config.ControllerIdBase; id <= byte.MaxValue; id++)
            {
                if (taken.Contains((byte)id))
                    continue;

                controllerId = (byte)id;
                return true;
            }

            controllerId = 0;
            return false;
        }

        // Finds the audio file next to the plugin or in either of its config folders.
        private string ResolveAudioPath(out List<string> searched) => ResolveAudioPath(Config.AudioFile, out searched);

        private string ResolveAudioPath(string configured, out List<string> searched)
        {
            searched = new List<string>();

            if (string.IsNullOrWhiteSpace(configured))
                return null;

            if (Path.IsPathRooted(configured))
            {
                searched.Add(configured);
                return File.Exists(configured) ? configured : null;
            }

            var directories = new List<string>();

            string pluginDirectory = string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(pluginDirectory))
                directories.Add(pluginDirectory);

            directories.Add(this.GetConfigDirectory().FullName);
            directories.Add(this.GetConfigDirectory(isGlobal: true).FullName);

            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory, configured);
                searched.Add(candidate);

                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool TryParseVariant(string raw, out ChaseVariant variant)
        {
            variant = ChaseVariant.NonTarget;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string normalised = raw.Trim().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
            return Enum.TryParse(normalised, ignoreCase: true, result: out variant);
        }
    }
}
