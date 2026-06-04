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

        // Multiply by 4 to work in quarter-band units, then check the fractional pattern.
        var quarters = clamped * 4.0;
        var roundedQuarters = Math.Round(quarters, MidpointRounding.AwayFromZero);
        // roundedQuarters is in {0, 1, 2, 3, ..., 36} representing 0.00, 0.25, 0.50, …, 9.00.
        // Snap to the nearest allowed band (whole or half):
        //   0 quarters  → 0.0       (whole)
        //   1 quarter   → 0.5       (.25 ↑)
        //   2 quarters  → 0.5       (whole half-band)
        //   3 quarters  → 1.0       (.75 ↑)
        //   4 quarters  → 1.0       (whole)
        var snapped = ((int)roundedQuarters) switch
        {
            var q when q % 4 == 0 => q / 4.0,                  // x.00
            var q when q % 4 == 1 => (q + 1) / 4.0,            // x.25 → x.5
            var q when q % 4 == 2 => q / 4.0,                  // x.50
            var q when q % 4 == 3 => (q + 1) / 4.0,            // x.75 → next whole
            _ => 0.0
        };

        return Math.Clamp(snapped, 0.0, 9.0);
    }
}
