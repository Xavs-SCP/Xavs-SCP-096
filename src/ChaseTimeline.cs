using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Scp096ChaseMusic
{
    // Which mix of the track a listener gets.
    public enum ChaseVariant
    {
        // What a player who is *not* one of SCP-096's targets hears.
        NonTarget,

        // What a player who *is* one of SCP-096's targets hears.
        Target
    }

    // The four musical sections, one per PlayerRoles.PlayableScps.Scp096.Scp096RageState.
    public enum ChaseSection
    {
        // Encounter / docile chase. Loops.
        Docile,

        // Enrage build-up. Plays once, then rolls straight into Enraged.
        BuildUp,

        // Enraged chase. Loops.
        Enraged,

        // Calming down. Plays once.
        Calming
    }

    // A half-open sample range [StartSample, StartSample + SampleCount) of the audio file.
    public readonly struct ChaseSegment
    {
        public readonly int StartSample;
        public readonly int SampleCount;

        public ChaseSegment(int startSample, int sampleCount)
        {
            StartSample = startSample;
            SampleCount = sampleCount;
        }

        public bool IsEmpty => SampleCount <= 0;
    }

    // Turns the configured section markers into sample ranges.
    //
    // Markers are treated as a flat, ordered list of cue points: whatever marker comes next in time ends the
    // previous one. That keeps the config honest even if someone reorders or retimes the sections.
    public sealed class ChaseTimeline
    {
        private readonly Dictionary<ChaseVariant, Dictionary<ChaseSection, ChaseSegment>> _segments =
            new Dictionary<ChaseVariant, Dictionary<ChaseSection, ChaseSegment>>();

        private ChaseTimeline()
        {
        }

        public static ChaseTimeline TryBuild(ChaseTimelineConfig config, int totalSamples, int sampleRate, out string error)
        {
            error = null;

            var markers = new List<(ChaseVariant Variant, ChaseSection Section, string Name, double Time)>();
            if (!TryAddMarker(markers, ChaseVariant.NonTarget, ChaseSection.Docile, nameof(config.NonTargetDocile), config.NonTargetDocile, ref error) ||
                !TryAddMarker(markers, ChaseVariant.NonTarget, ChaseSection.BuildUp, nameof(config.NonTargetEnrageBuildUp), config.NonTargetEnrageBuildUp, ref error) ||
                !TryAddMarker(markers, ChaseVariant.NonTarget, ChaseSection.Enraged, nameof(config.NonTargetChaseEnraged), config.NonTargetChaseEnraged, ref error) ||
                !TryAddMarker(markers, ChaseVariant.NonTarget, ChaseSection.Calming, nameof(config.NonTargetCalmingDown), config.NonTargetCalmingDown, ref error) ||
                !TryAddMarker(markers, ChaseVariant.Target, ChaseSection.Docile, nameof(config.TargetDocile), config.TargetDocile, ref error) ||
                !TryAddMarker(markers, ChaseVariant.Target, ChaseSection.BuildUp, nameof(config.TargetEnrageBuildUp), config.TargetEnrageBuildUp, ref error) ||
                !TryAddMarker(markers, ChaseVariant.Target, ChaseSection.Enraged, nameof(config.TargetChaseEnraged), config.TargetChaseEnraged, ref error) ||
                !TryAddMarker(markers, ChaseVariant.Target, ChaseSection.Calming, nameof(config.TargetCalmingDown), config.TargetCalmingDown, ref error))
            {
                return null;
            }

            double end;
            if (string.IsNullOrWhiteSpace(config.End))
            {
                end = (double)totalSamples / sampleRate;
            }
            else if (!TryParseTimestamp(config.End, out end))
            {
                error = "timeline.end: '" + config.End + "' is not a valid timestamp.";
                return null;
            }

            // Cue points in time order, so each section ends where the next one starts.
            double[] ordered = markers.Select(m => m.Time).Concat(new[] { end }).Distinct().OrderBy(t => t).ToArray();

            var timeline = new ChaseTimeline();
            foreach (var marker in markers)
            {
                double next = ordered.FirstOrDefault(t => t > marker.Time);
                if (next <= marker.Time)
                {
                    error = "timeline." + marker.Name + ": starts at or after the end of the track (" +
                            marker.Time.ToString("0.###", CultureInfo.InvariantCulture) + "s).";
                    return null;
                }

                int startSample = (int)Math.Round(marker.Time * sampleRate);
                int endSample = (int)Math.Round(next * sampleRate);
                startSample = Clamp(startSample, 0, totalSamples);
                endSample = Clamp(endSample, 0, totalSamples);

                if (endSample - startSample <= 0)
                {
                    error = "timeline." + marker.Name + ": resolves to an empty section. Is the audio file too short?";
                    return null;
                }

                if (!timeline._segments.TryGetValue(marker.Variant, out var perSection))
                {
                    perSection = new Dictionary<ChaseSection, ChaseSegment>();
                    timeline._segments[marker.Variant] = perSection;
                }

                perSection[marker.Section] = new ChaseSegment(startSample, endSample - startSample);
            }

            return timeline;
        }

        public ChaseSegment Get(ChaseVariant variant, ChaseSection section) => _segments[variant][section];

        private static bool TryAddMarker(List<(ChaseVariant, ChaseSection, string, double)> markers, ChaseVariant variant,
            ChaseSection section, string name, string raw, ref string error)
        {
            if (!TryParseTimestamp(raw, out double time))
            {
                error = "timeline." + name + ": '" + raw + "' is not a valid timestamp.";
                return false;
            }

            markers.Add((variant, section, name, time));
            return true;
        }

        // Parses m:ss, m:ss.fff, h:mm:ss or a plain number of seconds.
        public static bool TryParseTimestamp(string raw, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string[] parts = raw.Trim().Split(':');
            if (parts.Length > 3)
                return false;

            double total = 0;
            foreach (string part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value < 0)
                    return false;

                total = total * 60 + value;
            }

            seconds = total;
            return true;
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
    }
}
