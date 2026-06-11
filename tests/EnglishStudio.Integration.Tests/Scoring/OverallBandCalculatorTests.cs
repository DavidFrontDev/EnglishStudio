using EnglishStudio.Modules.Ielts.Core.Scoring;
using Xunit;

namespace EnglishStudio.Integration.Tests.Scoring;

public class OverallBandCalculatorTests
{
    // Official IELTS rule: round to the nearest half band; exact .25 / .75 midpoints round UP.
    // The .125 / .625 cases are regression tests for the historical double-rounding bug
    // (6.125 used to come out as 6.5).
    [Theory]
    [InlineData(6.0, 6.0)]
    [InlineData(6.125, 6.0)]
    [InlineData(6.25, 6.5)]
    [InlineData(6.375, 6.5)]
    [InlineData(6.5, 6.5)]
    [InlineData(6.625, 6.5)]
    [InlineData(6.75, 7.0)]
    [InlineData(6.875, 7.0)]
    [InlineData(7.0, 7.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(9.0, 9.0)]
    [InlineData(8.875, 9.0)]
    public void RoundToOfficialBand_UsesOfficialIeltsRule(double average, double expected)
    {
        Assert.Equal(expected, OverallBandCalculator.RoundToOfficialBand(average));
    }

    [Theory]
    [InlineData(6.5, 6.0, 6.0, 6.0, 6.0)]   // avg 6.125 → 6.0 (regression: was 6.5)
    [InlineData(7.0, 6.5, 6.5, 6.5, 6.5)]   // avg 6.625 → 6.5 (regression: was 7.0)
    [InlineData(6.5, 6.5, 6.0, 6.0, 6.5)]   // avg 6.25 → 6.5 (midpoint rounds up)
    [InlineData(7.0, 7.0, 6.5, 6.5, 7.0)]   // avg 6.75 → 7.0 (midpoint rounds up)
    [InlineData(8.0, 8.0, 8.0, 8.0, 8.0)]
    public void Calculate_FourSections(double l, double r, double w, double s, double expected)
    {
        Assert.Equal(expected, OverallBandCalculator.Calculate(l, r, w, s));
    }

    [Fact]
    public void RoundToOfficialBand_NanReturnsZero()
    {
        Assert.Equal(0.0, OverallBandCalculator.RoundToOfficialBand(double.NaN));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(10.0, 9.0)]
    public void RoundToOfficialBand_ClampsToValidRange(double input, double expected)
    {
        Assert.Equal(expected, OverallBandCalculator.RoundToOfficialBand(input));
    }
}
