namespace Scp096ChaseMusic
{
    // Turns a volume percentage reported by a client into a gain multiplier.
    //
    // Split out from the settings so it can be tested without the game. It exists because the value is not
    // trustworthy: SSSliderSetting.DeserializeValue reads the float straight off the wire with no validation,
    // so a modified client can report anything, including values that are not numbers at all.
    public static class VolumeMath
    {
        // Converts a reported percentage to a 0..max gain.
        //
        // False when the value is unusable, so the caller can fall back to its own default.
        public static bool TryGetFraction(float reportedPercent, float maximumPercent, out float fraction)
        {
            fraction = 0f;

            // NaN is the dangerous one: it compares false against everything, so it would slip past a range
            // check, poison the "has the volume changed" test and make the speaker resync every tick forever.
            if (float.IsNaN(reportedPercent) || float.IsInfinity(reportedPercent))
                return false;

            if (reportedPercent < 0f)
                reportedPercent = 0f;
            else if (reportedPercent > maximumPercent)
                reportedPercent = maximumPercent;

            fraction = reportedPercent / 100f;
            return true;
        }
    }
}
