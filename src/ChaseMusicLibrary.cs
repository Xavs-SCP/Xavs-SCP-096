using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Scp096ChaseMusic
{
    // The decoded sections of the chase track, ready to hand to the game's audio transmitter.
    //
    // Only the configured variant is loaded: the sections are read straight out of the file by byte offset, so
    // the unused half of the track never touches memory. All SCP-096 players share these arrays; the
    // transmitter only ever reads from them.
    public sealed class ChaseMusicLibrary
    {
        // The sample rate the game's voice pipeline expects. Anything else would play at the wrong pitch.
        public const int RequiredSampleRate = 48000;

        private readonly Dictionary<ChaseSection, float[]> _sections;

        public ChaseVariant Variant { get; }
        public string SourcePath { get; }

        private ChaseMusicLibrary(ChaseVariant variant, string sourcePath, Dictionary<ChaseSection, float[]> sections)
        {
            Variant = variant;
            SourcePath = sourcePath;
            _sections = sections;
        }

        public float[] this[ChaseSection section] => _sections[section];

        public double DurationOf(ChaseSection section) => (double)_sections[section].Length / RequiredSampleRate;

        // Loads the four sections of variant from path.
        //
        // The loaded library, or null with error describing what went wrong.
        public static ChaseMusicLibrary TryLoad(string path, ChaseVariant variant, ChaseTimelineConfig timelineConfig, out string error)
        {
            using (WavAudioFile file = WavAudioFile.TryOpen(path, out error))
            {
                if (file == null)
                {
                    error = "could not read '" + path + "': " + error;
                    return null;
                }

                if (file.SampleRate != RequiredSampleRate)
                {
                    error = "'" + Path.GetFileName(path) + "' is " + file.SampleRate + " Hz; the game needs " +
                            RequiredSampleRate + " Hz. Re-encode it, e.g. " +
                            "ffmpeg -i input.mp3 -ac 1 -ar 48000 -c:a pcm_s16le " + Path.GetFileName(path);
                    return null;
                }

                ChaseTimeline timeline = ChaseTimeline.TryBuild(timelineConfig, file.FrameCount, file.SampleRate, out error);
                if (timeline == null)
                    return null;

                var sections = new Dictionary<ChaseSection, float[]>();
                foreach (ChaseSection section in new[] { ChaseSection.Docile, ChaseSection.BuildUp, ChaseSection.Enraged, ChaseSection.Calming })
                {
                    ChaseSegment segment = timeline.Get(variant, section);
                    try
                    {
                        sections[section] = file.ReadMono(segment.StartSample, segment.SampleCount);
                    }
                    catch (Exception ex)
                    {
                        error = "failed to read the " + section + " section: " + ex.Message;
                        return null;
                    }
                }

                return new ChaseMusicLibrary(variant, path, sections);
            }
        }

        // Builds a library from samples that are already in memory, for callers that decoded the audio
        // themselves and for tests that need crisp, synthetic sections.
        public static ChaseMusicLibrary FromSections(ChaseVariant variant, IDictionary<ChaseSection, float[]> sections)
        {
            if (sections == null)
                throw new ArgumentNullException(nameof(sections));

            var copy = new Dictionary<ChaseSection, float[]>();
            foreach (ChaseSection section in new[] { ChaseSection.Docile, ChaseSection.BuildUp, ChaseSection.Enraged, ChaseSection.Calming })
            {
                if (!sections.TryGetValue(section, out float[] samples) || samples == null || samples.Length == 0)
                    throw new ArgumentException("Missing samples for the " + section + " section.", nameof(sections));

                copy[section] = samples;
            }

            return new ChaseMusicLibrary(variant, "<in memory>", copy);
        }

        // One line per section, for the startup log.
        public string Describe()
        {
            var parts = new List<string>();
            foreach (var pair in _sections)
            {
                parts.Add(pair.Key + " " +
                          ((double)pair.Value.Length / RequiredSampleRate).ToString("0.00", CultureInfo.InvariantCulture) + "s");
            }

            return string.Join(", ", parts.ToArray());
        }
    }
}
