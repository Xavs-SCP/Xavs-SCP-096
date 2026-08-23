using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace Scp096ChaseMusic
{
    // The plugin's own entries in the client's server-specific settings tab: a volume slider and a Bass toggle.
    //
    // The game's own "Chase Themes" slider cannot drive any of this. It is not an audio mixer group - the five
    // mixer sliders are Master, VoiceChat, SoundEffects, MenuMusic and MenuUI - but a plain local
    // UserSetting<float> that ChaseThemeSoundtrack reads in code. Nothing sends it to the server: the only
    // client-to-server settings traffic is SSSUpdateMessage for these entries and SSSUserStatusReport, which
    // carries just a version number and whether the tab is open.
    //
    // So these are the server-side equivalent, and they are per player: two people playing SCP-096 each get
    // their own volume and their own choice of mix.
    public static class ChaseMusicSettings
    {
        private const float SliderMinimum = 0f;

        // The game's own Chase Themes slider tops out at 200%; this goes further by request.
        private const float SliderMaximum = 300f;

        private static SSGroupHeader _header;
        private static SSSliderSetting _volume;
        private static SSTwoButtonsSetting _bass;

        public static bool IsRegistered => _volume != null;

        // Adds the entries to whatever settings the server already defines and pushes them out.
        //
        // Appends rather than assigns: DefinedSettings is shared server-wide, so replacing it would silently
        // delete the settings of every other plugin.
        public static void Register(int volumeId, float defaultPercent, bool offerBass, int bassId, bool bassByDefault)
        {
            if (IsRegistered)
                return;

            _header = new SSGroupHeader("SCP-096 Chase Music");
            _volume = new SSSliderSetting(
                volumeId,
                "Chase music volume",
                SliderMinimum,
                SliderMaximum,
                Mathf.Clamp(defaultPercent, SliderMinimum, SliderMaximum),
                integer: true,
                valueToStringFormat: "0",
                finalDisplayFormat: "{0}%",
                hint: "How loud your own chase music is while you are SCP-096. Only affects you. " +
                      "100% is the intended level; the track is mastered close to the ceiling, so much " +
                      "above that will clip.");

            var entries = new List<ServerSpecificSettingBase> { _header, _volume };

            if (offerBass)
            {
                _bass = new SSTwoButtonsSetting(
                    bassId,
                    "Bass",
                    "Off",
                    "On",
                    bassByDefault,
                    "Swaps to the bass-heavy mix of the chase music. Takes effect at the next section.");

                entries.Add(_bass);
            }

            ServerSpecificSettingBase[] existing =
                ServerSpecificSettingsSync.DefinedSettings ?? Array.Empty<ServerSpecificSettingBase>();

            ServerSpecificSettingsSync.DefinedSettings = existing.Concat(entries).ToArray();
            ServerSpecificSettingsSync.SendToAll();
        }

        // Takes the entries back out again, leaving any other plugin's settings alone.
        public static void Unregister()
        {
            if (!IsRegistered)
                return;

            ServerSpecificSettingBase[] existing = ServerSpecificSettingsSync.DefinedSettings;
            if (existing != null)
            {
                ServerSpecificSettingsSync.DefinedSettings = existing
                    .Where(setting => setting != _header && setting != _volume && setting != _bass)
                    .ToArray();

                ServerSpecificSettingsSync.SendToAll();
            }

            _header = null;
            _volume = null;
            _bass = null;
        }

        // The volume to use for one player: their slider position scaled by the server's configured level.
        //
        // configuredVolume unchanged when the slider is off or the player has not reported a value yet, so
        // behaviour without it is exactly as before.
        public static float ResolveVolume(ReferenceHub hub, float configuredVolume)
        {
            if (!IsRegistered || hub == null)
                return configuredVolume;

            if (!ServerSpecificSettingsSync.TryGetSettingOfUser(hub, _volume.SettingId, out SSSliderSetting setting))
                return configuredVolume;

            // Not clamped to 1: like the game's slider, 100% is unity and above is overdrive. Clamped to the
            // range actually offered, though, because nothing in the game enforces it.
            return VolumeMath.TryGetFraction(setting.SyncFloatValue, SliderMaximum, out float fraction)
                ? configuredVolume * fraction
                : configuredVolume;
        }

        public static bool WantsBass(ReferenceHub hub)
        {
            if (_bass == null || hub == null)
                return false;

            return ServerSpecificSettingsSync.TryGetSettingOfUser(hub, _bass.SettingId, out SSTwoButtonsSetting setting)
                ? setting.SyncIsB
                : _bass.DefaultIsB;
        }
    }
}
