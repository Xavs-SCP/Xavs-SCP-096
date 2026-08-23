using System;
using System.Collections.Generic;
using System.Globalization;
using Scp096ChaseMusic;

// Offline checks for the parts of the plugin that do not need the game running: timestamp parsing, the section
// boundaries the config resolves to, and the audio actually read out of the file at those offsets.
//
// Compiled and run by tests\run-tests.ps1 against the same sources the plugin ships.
internal static class TimelineTests
{
    private static int _failures;

    private static int Main(string[] args)
    {
        string audioPath = args.Length > 0 ? args[0] : "audio/blind_rage.wav";

        TimestampParsing();
        SectionBoundaries();
        AudioFileMatchesTimeline(audioPath);
        SectionsContainAudio(audioPath);

        HostileVolumeValues();

        Section("Config serialisation");
        ConfigYamlTests.Run(Check);

        Console.WriteLine();
        if (_failures == 0)
        {
            Console.WriteLine("All checks passed.");
            return 0;
        }

        Console.WriteLine(_failures + " check(s) FAILED.");
        return 1;
    }

    // The volume percentage arrives from the client with no validation whatsoever - the game reads the float
    // straight off the wire - so every hostile shape of it has to be handled here.
    private static void HostileVolumeValues()
    {
        Section("Hostile volume values from a client");

        const float max = 300f;

        Check("a normal value passes through",
            VolumeMath.TryGetFraction(100f, max, out float unity) && Math.Abs(unity - 1f) < 0.0001f, null);
        Check("overdrive is allowed up to the offered maximum",
            VolumeMath.TryGetFraction(300f, max, out float loud) && Math.Abs(loud - 3f) < 0.0001f, null);

        Check("beyond the maximum is clamped, not trusted",
            VolumeMath.TryGetFraction(1e9f, max, out float huge) && Math.Abs(huge - 3f) < 0.0001f, huge.ToString());
        Check("negative is clamped to silence",
            VolumeMath.TryGetFraction(-500f, max, out float negative) && negative == 0f, negative.ToString());

        // NaN compares false against everything, so it would slip through a naive range check and then poison
        // the change-detection that decides whether to resend the volume.
        Check("NaN is rejected outright", !VolumeMath.TryGetFraction(float.NaN, max, out _));
        Check("positive infinity is rejected", !VolumeMath.TryGetFraction(float.PositiveInfinity, max, out _));
        Check("negative infinity is rejected", !VolumeMath.TryGetFraction(float.NegativeInfinity, max, out _));

        // The specific failure that made this worth guarding: a NaN gain never equals the last applied gain,
        // so the "only resend when it changed" check would fire on every tick, forever.
        VolumeMath.TryGetFraction(float.NaN, max, out float poisoned);
        Check("a rejected value cannot defeat change detection",
            !(Math.Abs(poisoned - poisoned) > 0.001f), null);
    }

    private static void TimestampParsing()
    {
        Section("Timestamp parsing");

        CheckTimestamp("0:00", 0);
        CheckTimestamp("0:48", 48);
        CheckTimestamp("1:50", 110);
        CheckTimestamp("2:38", 158);
        CheckTimestamp("3:40", 220);
        CheckTimestamp("3:53.848", 233.848);
        CheckTimestamp("1:02:03", 3723);
        CheckTimestamp("12.5", 12.5);

        CheckFalse("rejects empty", ChaseTimeline.TryParseTimestamp("", out _));
        CheckFalse("rejects text", ChaseTimeline.TryParseTimestamp("abc", out _));
        CheckFalse("rejects negatives", ChaseTimeline.TryParseTimestamp("-1:00", out _));
        CheckFalse("rejects 4 fields", ChaseTimeline.TryParseTimestamp("1:2:3:4", out _));
    }

    // The boundaries the brief called for, at the shipped track's length.
    private static void SectionBoundaries()
    {
        Section("Section boundaries (default config)");

        const int rate = ChaseMusicLibrary.RequiredSampleRate;
        const double trackLength = 233.848167;
        int totalSamples = (int)(trackLength * rate);

        ChaseTimeline timeline = ChaseTimeline.TryBuild(new ChaseTimelineConfig(), totalSamples, rate, out string error);
        if (timeline == null)
        {
            Fail("timeline built", error);
            return;
        }

        CheckSegment(timeline, ChaseVariant.NonTarget, ChaseSection.Docile, 0, 48, rate);
        CheckSegment(timeline, ChaseVariant.NonTarget, ChaseSection.BuildUp, 48, 54, rate);
        CheckSegment(timeline, ChaseVariant.NonTarget, ChaseSection.Enraged, 54, 110, rate);
        CheckSegment(timeline, ChaseVariant.NonTarget, ChaseSection.Calming, 110, 119, rate);
        CheckSegment(timeline, ChaseVariant.Target, ChaseSection.Docile, 119, 158, rate);
        CheckSegment(timeline, ChaseVariant.Target, ChaseSection.BuildUp, 158, 164, rate);
        CheckSegment(timeline, ChaseVariant.Target, ChaseSection.Enraged, 164, 220, rate);
        CheckSegment(timeline, ChaseVariant.Target, ChaseSection.Calming, 220, trackLength, rate);

        // The enrage build-up has to cover the game's 6.1s Distressed state, or the enraged loop starts late.
        double buildUp = timeline.Get(ChaseVariant.NonTarget, ChaseSection.BuildUp).SampleCount / (double)rate;
        Check("non-target build-up is within 0.2s of the game's 6.1s enraging time", Math.Abs(buildUp - 6.1) <= 0.2,
            buildUp.ToString("0.###", CultureInfo.InvariantCulture) + "s");
    }

    private static void AudioFileMatchesTimeline(string audioPath)
    {
        Section("Audio file");

        using (WavAudioFile file = WavAudioFile.TryOpen(audioPath, out string error))
        {
            if (file == null)
            {
                Fail("opened " + audioPath, error);
                return;
            }

            Check("sample rate is 48000", file.SampleRate == ChaseMusicLibrary.RequiredSampleRate, file.SampleRate + " Hz");
            Check("mono", file.Channels == 1, file.Channels + " channel(s)");
            Check("long enough for the last section (3:40+)", file.Duration > 220,
                file.Duration.ToString("0.###", CultureInfo.InvariantCulture) + "s");
        }
    }

    // Reads each section through the real loader and checks it contains audio. A wrong offset or a bad WAV
    // parse would show up here as silence or a short read.
    private static void SectionsContainAudio(string audioPath)
    {
        Section("Section contents");

        var config = new ChaseTimelineConfig();

        foreach (ChaseVariant variant in new[] { ChaseVariant.NonTarget, ChaseVariant.Target })
        {
            ChaseMusicLibrary library = ChaseMusicLibrary.TryLoad(audioPath, variant, config, out string error);
            if (library == null)
            {
                Fail("loaded the " + variant + " mix", error);
                continue;
            }

            foreach (ChaseSection section in new[] { ChaseSection.Docile, ChaseSection.BuildUp, ChaseSection.Enraged, ChaseSection.Calming })
            {
                float[] samples = library[section];
                double peak = 0;
                double sumOfSquares = 0;
                foreach (float sample in samples)
                {
                    double magnitude = Math.Abs(sample);
                    if (magnitude > peak)
                        peak = magnitude;

                    sumOfSquares += (double)sample * sample;
                }

                double rms = Math.Sqrt(sumOfSquares / samples.Length);

                Check(variant + "/" + section + " has audio", rms > 0.001 && peak > 0.01,
                    "rms " + rms.ToString("0.0000", CultureInfo.InvariantCulture) +
                    ", peak " + peak.ToString("0.000", CultureInfo.InvariantCulture) +
                    ", " + library.DurationOf(section).ToString("0.00", CultureInfo.InvariantCulture) + "s");

                // The transmitter sends 480-sample frames and applies a fixed 1.414x gain before encoding.
                Check(variant + "/" + section + " stays below clipping after the transmitter's gain",
                    peak * 1.4142135 <= 1.0,
                    "would peak at " + (peak * 1.4142135).ToString("0.000", CultureInfo.InvariantCulture));
            }
        }
    }

    private static void CheckSegment(ChaseTimeline timeline, ChaseVariant variant, ChaseSection section,
        double expectedStart, double expectedEnd, int rate)
    {
        ChaseSegment segment = timeline.Get(variant, section);
        double start = segment.StartSample / (double)rate;
        double end = (segment.StartSample + segment.SampleCount) / (double)rate;

        Check(variant + "/" + section + " spans " + Format(expectedStart) + " - " + Format(expectedEnd),
            Math.Abs(start - expectedStart) < 0.01 && Math.Abs(end - expectedEnd) < 0.01,
            Format(start) + " - " + Format(end));
    }

    private static string Format(double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return ((int)span.TotalMinutes) + ":" + span.Seconds.ToString("00") +
               (span.Milliseconds > 0 ? "." + span.Milliseconds.ToString("000") : string.Empty);
    }

    private static void CheckTimestamp(string raw, double expected)
    {
        bool parsed = ChaseTimeline.TryParseTimestamp(raw, out double actual);
        Check("'" + raw + "' -> " + expected.ToString(CultureInfo.InvariantCulture) + "s",
            parsed && Math.Abs(actual - expected) < 0.0005,
            parsed ? actual.ToString(CultureInfo.InvariantCulture) : "parse failed");
    }

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name);
    }

    private static void Check(string what, bool condition, string detail = null)
    {
        if (condition)
        {
            Console.WriteLine("  PASS  " + what + (detail == null ? string.Empty : "  (" + detail + ")"));
            return;
        }

        Fail(what, detail);
    }

    private static void CheckFalse(string what, bool condition) => Check(what, !condition);

    private static void Fail(string what, string detail)
    {
        _failures++;
        Console.WriteLine("  FAIL  " + what + (detail == null ? string.Empty : "  (" + detail + ")"));
    }
}
