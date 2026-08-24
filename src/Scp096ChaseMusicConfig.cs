using System.ComponentModel;

namespace Scp096ChaseMusic
{
    // Everything in this file lands in %AppData%\SCP Secret
    // Laboratory\LabAPI\configs\<port>\Scp096ChaseMusic\config.yml.
    public class Scp096ChaseMusicConfig
    {
        [Description("Set to false to turn the plugin off without removing it.")]
        public bool IsEnabled { get; set; } = true;

        [Description("WAV to play. Must be 48000 Hz. Looked for next to the DLL, then in the config folders.")]
        public string AudioFile { get; set; } = "blind_rage.wav";

        [Description("Which mix to use: NonTarget or Target. 096 is never their own target, so NonTarget " +
                     "is what a bystander would hear.")]
        public string Variant { get; set; } = "NonTarget";

        [Description("Volume for SCP-096. 1.0 is the file's own level.")]
        // decimal, not float: YamlDotNet writes floats and doubles back out with their binary rounding error, so
        // a hand-edited 'volume: 0.7' would be rewritten as 0.69999999988 the next time the config is saved.
        public decimal Volume { get; set; } = 1.0m;

        [Description("Wait for 096 to have a target before starting the music. False loops the docile " +
                     "section all round.")]
        public bool DocileRequiresTargets { get; set; } = true;

        [Description("Let the calm-down finish even after 096 is docile again. The state only lasts 5s, " +
                     "the music runs longer.")]
        public bool PlayCalmingTailInFull { get; set; } = true;

        [Description("Fade-in time when a chase starts. 0 for no fade.")]
        public decimal FadeInSeconds { get; set; } = 1.5m;

        [Description("Fade-out time when a chase ends, 096 dies, or the round restarts.")]
        public decimal FadeOutSeconds { get; set; } = 2.0m;

        [Description("Second mix, offered to players as a Bass tick box. Blank or missing hides the option.")]
        public string BassAudioFile { get; set; } = "blind_rage_bass.wav";

        [Description("Whether Bass starts ticked for players who have not chosen.")]
        public bool BassOnByDefault { get; set; } = false;

        [Description("Setting id for the Bass tick box. Change it only if another plugin uses the same one.")]
        public int BassToggleId { get; set; } = 96002;

        [Description("Give players a volume slider in their server-specific settings. The game's own Chase " +
                     "Themes slider never reaches the server, so this is the only way they can adjust it.")]
        public bool ShowVolumeSlider { get; set; } = true;

        [Description("Where that slider starts, 0 - 100. It scales the volume above.")]
        public int VolumeSliderDefaultPercent { get; set; } = 100;

        [Description("Setting id for the volume slider. Change it only if another plugin uses the same one.")]
        public int VolumeSliderId { get; set; } = 96001;

        [Description("First speaker id to hand out, one per live 096. Change it only if another audio plugin " +
                     "already uses that range.")]
        public byte ControllerIdBase { get; set; } = 96;

        [Description("How often to check 096's rage state, in seconds. Lower reacts faster and costs a little more.")]
        public decimal TickInterval { get; set; } = 0.05m;

        [Description("Print every section change to the console.")]
        public bool VerboseLogging { get; set; } = false;

        [Description("Where each section starts. A section runs until the next one begins. " +
                     "Takes m:ss, m:ss.fff, h:mm:ss or seconds.")]
        public ChaseTimelineConfig Timeline { get; set; } = new ChaseTimelineConfig();
    }

    // Marker list for "Blind Rage". Defaults are the shipped track's section boundaries.
    public class ChaseTimelineConfig
    {
        [Description("Encounter and docile chase, non-target mix.")]
        public string NonTargetDocile { get; set; } = "0:00";

        [Description("Enrage build-up, non-target mix.")]
        public string NonTargetEnrageBuildUp { get; set; } = "0:48";

        [Description("Enraged chase, non-target mix. Loops while 096 stays enraged.")]
        public string NonTargetChaseEnraged { get; set; } = "0:54";

        [Description("Calm-down, non-target mix.")]
        public string NonTargetCalmingDown { get; set; } = "1:50";

        [Description("Face seen and docile chase, target mix.")]
        public string TargetDocile { get; set; } = "1:59";

        [Description("Enrage build-up, target mix.")]
        public string TargetEnrageBuildUp { get; set; } = "2:38";

        [Description("Enraged chase, target mix. Loops while 096 stays enraged.")]
        public string TargetChaseEnraged { get; set; } = "2:44";

        [Description("Calm-down, target mix.")]
        public string TargetCalmingDown { get; set; } = "3:40";

        [Description("Where the last section ends. Leave blank to run to the end of the file.")]
        public string End { get; set; } = string.Empty;
    }
}
