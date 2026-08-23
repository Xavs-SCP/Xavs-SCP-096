using System.ComponentModel;

namespace Scp096ChaseMusic
{
    // Everything in this file lands in %AppData%\SCP Secret
    // Laboratory\LabAPI\configs\<port>\Scp096ChaseMusic\config.yml.
    public class Scp096ChaseMusicConfig
    {
        [Description("Set to false to disable the plugin without removing it.")]
        public bool IsEnabled { get; set; } = true;

        [Description("Audio file to stream. Must be a WAV (PCM 16/24/32-bit or 32-bit float) at 48000 Hz. " +
                     "A relative name is looked up next to the plugin DLL and in the plugin's config folders.")]
        public string AudioFile { get; set; } = "blind_rage.wav";

        [Description("Which mix SCP-096 hears: 'NonTarget' (what a bystander hears) or 'Target' (what someone " +
                     "096 is hunting hears). SCP-096 is never their own target, so 'NonTarget' is the faithful choice.")]
        public string Variant { get; set; } = "NonTarget";

        [Description("Playback volume for SCP-096, 0.0 - 5.0. 1.0 plays the file at its own level. This is a " +
                     "client side multiplier applied after decoding, so raising it never distorts the stream.")]
        // decimal, not float: YamlDotNet writes floats and doubles back out with their binary rounding error, so
        // a hand-edited 'volume: 0.7' would be rewritten as 0.69999999988 the next time the config is saved.
        public decimal Volume { get; set; } = 1.0m;

        [Description("Only play the docile/encounter section once SCP-096 has at least one target. " +
                     "Set to false to loop the docile section for the whole round.")]
        public bool DocileRequiresTargets { get; set; } = true;

        [Description("Let the 'calming down' section play out in full even after SCP-096 has returned to the " +
                     "docile state (the state lasts 5s, the music tail is longer).")]
        public bool PlayCalmingTailInFull { get; set; } = true;

        [Description("Seconds to fade the music in when a chase starts. 0 starts it at full volume.")]
        public decimal FadeInSeconds { get; set; } = 1.5m;

        [Description("Seconds to fade the music out when a chase ends, SCP-096 dies, or the round restarts.")]
        public decimal FadeOutSeconds { get; set; } = 2.0m;

        [Description("Bass-heavy alternative mix, offered to players as a \"Bass\" tick box. " +
                     "Leave the filename blank, or delete the file, to hide the option.")]
        public string BassAudioFile { get; set; } = "blind_rage_bass.wav";

        [Description("Whether the Bass tick box starts ticked for players who have not chosen.")]
        public bool BassOnByDefault { get; set; } = false;

        [Description("Id for the Bass tick box. Only change it if another plugin on this server already uses it.")]
        public int BassToggleId { get; set; } = 96002;

        [Description("Show each player a 'Chase music volume' slider in their server-specific settings tab. " +
                     "The game's own Chase Themes slider is a local client setting the server cannot read, so this " +
                     "is how a player adjusts their own chase music.")]
        public bool ShowVolumeSlider { get; set; } = true;

        [Description("Where that slider starts, 0 - 100. It scales the volume above, so 100 means the volume set here.")]
        public int VolumeSliderDefaultPercent { get; set; } = 100;

        [Description("Id for the volume slider. Only change it if another plugin on this server already uses it.")]
        public int VolumeSliderId { get; set; } = 96001;

        [Description("First speaker controller id to hand out. One id is used per live SCP-096. Only change this " +
                     "if another audio plugin on this server already claims ids in the 96-... range.")]
        public byte ControllerIdBase { get; set; } = 96;

        [Description("How often (seconds) SCP-096's rage state is polled. Lower reacts faster, costs slightly more.")]
        public decimal TickInterval { get; set; } = 0.05m;

        [Description("Log every music section change to the server console.")]
        public bool VerboseLogging { get; set; } = false;

        [Description("Section start times inside the audio file. Each section runs until the next one begins. " +
                     "Accepts 'm:ss', 'm:ss.fff', 'h:mm:ss' or plain seconds.")]
        public ChaseTimelineConfig Timeline { get; set; } = new ChaseTimelineConfig();
    }

    // Marker list for "Blind Rage". Defaults are the shipped track's section boundaries.
    public class ChaseTimelineConfig
    {
        [Description("Encounter (Docile) | Chase (Docile), non-target mix.")]
        public string NonTargetDocile { get; set; } = "0:00";

        [Description("Enrage build-up, non-target mix.")]
        public string NonTargetEnrageBuildUp { get; set; } = "0:48";

        [Description("Chase (Enraged), non-target mix. Loops for as long as SCP-096 stays enraged.")]
        public string NonTargetChaseEnraged { get; set; } = "0:54";

        [Description("Calming down, non-target mix.")]
        public string NonTargetCalmingDown { get; set; } = "1:50";

        [Description("Face seen | Chase (Docile), target mix.")]
        public string TargetDocile { get; set; } = "1:59";

        [Description("Enrage build-up, target mix.")]
        public string TargetEnrageBuildUp { get; set; } = "2:38";

        [Description("Chase (Enraged), target mix. Loops for as long as SCP-096 stays enraged.")]
        public string TargetChaseEnraged { get; set; } = "2:44";

        [Description("Calming down, target mix.")]
        public string TargetCalmingDown { get; set; } = "3:40";

        [Description("Where the last section ends. Leave empty to run to the end of the file.")]
        public string End { get; set; } = string.Empty;
    }
}
