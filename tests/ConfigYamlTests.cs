using System;

#if YAML
using System.Linq;
using Scp096ChaseMusic;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
#endif

// Round-trips the config through the same YamlDotNet setup LabAPI uses, so a property that the serializer
// cannot handle shows up here instead of as an unreadable config.yml on a live server.
//
// Only compiled when the build script found the server's YamlDotNet.dll (-define:YAML).
internal static class ConfigYamlTests
{
    public static void Run(Action<string, bool, string> check)
    {
#if YAML
        // Mirrors LabApi.Loader.Features.Yaml.YamlConfigParser: underscored keys, no aliases, properties only.
        ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .DisableAliases()
            .IgnoreFields()
            .Build();

        IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .IgnoreFields()
            .Build();

        var original = new Scp096ChaseMusicConfig();
        string yaml = serializer.Serialize(original);

        Console.WriteLine();
        foreach (string line in yaml.Split('\n').Select(l => l.TrimEnd()))
            Console.WriteLine("    | " + line);

        Console.WriteLine();

        foreach (string key in new[]
                 {
                     "is_enabled", "audio_file", "variant", "volume", "docile_requires_targets",
                     "play_calming_tail_in_full", "controller_id_base", "tick_interval", "verbose_logging",
                     "timeline", "non_target_docile", "non_target_enrage_build_up", "non_target_chase_enraged",
                     "non_target_calming_down", "target_docile", "target_enrage_build_up", "target_chase_enraged",
                     "target_calming_down"
                 })
        {
            check("config.yml contains '" + key + "'", yaml.Contains(key + ":"), null);
        }

        Scp096ChaseMusicConfig reloaded = deserializer.Deserialize<Scp096ChaseMusicConfig>(yaml);

        check("round-trips is_enabled", reloaded.IsEnabled == original.IsEnabled, null);
        check("round-trips audio_file", reloaded.AudioFile == original.AudioFile, reloaded.AudioFile);
        check("round-trips variant", reloaded.Variant == original.Variant, reloaded.Variant);
        check("round-trips volume", reloaded.Volume == original.Volume, reloaded.Volume.ToString());
        check("round-trips controller_id_base", reloaded.ControllerIdBase == original.ControllerIdBase, null);
        check("round-trips tick_interval", reloaded.TickInterval == original.TickInterval, null);

        // Numbers have to survive a save/load cycle unmangled, or LabAPI rewrites the user's config with noise.
        check("tick_interval serialises cleanly", yaml.Contains("tick_interval: 0.05" + Environment.NewLine), null);

        Scp096ChaseMusicConfig quieter = deserializer.Deserialize<Scp096ChaseMusicConfig>(
            yaml.Replace("volume: 1.0" + Environment.NewLine, "volume: 0.7" + Environment.NewLine));
        check("a hand-edited volume survives being saved again",
            serializer.Serialize(quieter).Contains("volume: 0.7" + Environment.NewLine),
            quieter.Volume.ToString());
        check("round-trips the timeline", reloaded.Timeline != null &&
                                          reloaded.Timeline.NonTargetDocile == original.Timeline.NonTargetDocile &&
                                          reloaded.Timeline.TargetCalmingDown == original.Timeline.TargetCalmingDown, null);

        // An edited config has to survive the same trip.
        string edited = yaml
            .Replace("variant: NonTarget", "variant: Target")
            .Replace("non_target_enrage_build_up: 0:48", "non_target_enrage_build_up: '0:47.5'");

        Scp096ChaseMusicConfig customised = deserializer.Deserialize<Scp096ChaseMusicConfig>(edited);
        check("accepts an edited variant", customised.Variant == "Target", customised.Variant);
        check("accepts a fractional timestamp", customised.Timeline.NonTargetEnrageBuildUp == "0:47.5",
            customised.Timeline.NonTargetEnrageBuildUp);
#else
        Console.WriteLine("  SKIP  YAML round-trip (YamlDotNet.dll not found; pass -ServerPath to run-tests.ps1)");
#endif
    }
}
