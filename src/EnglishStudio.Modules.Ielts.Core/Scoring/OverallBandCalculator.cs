namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Computes the overall IELTS band from the four section bands using the official rounding rule:
///   • If the average ends in .25 → round up to the next half band.
///   • If the average ends in .75 → round up to the next whole band.
///   • Otherwise round to the nearest half or whole band.
/// </summary>
public static class OverallBandCalculator
{
    public static double Calculate(double listening, double reading, double writing, double speaking)
    {
        var average = (listening + reading + writing + speaking) / 4.0;
        return RoundToOfficialBand(average);
    }

    public static double RoundToOfficialBand(double average)
    {
        if (double.IsNaN(average)) return 0.0;

        var clamped = Math.Clamp(average, 0.0, 9.0);

        // Round to the nearest half band in one step; exact midpoints (.25/.75) round up
        // (AwayFromZero), matching the official rule.
        var snapped = Math.Round(clamped * 2.0, MidpointRounding.AwayFromZero) / 2.0;

        return Math.Clamp(snapped, 0.0, 9.0);
    }
}
