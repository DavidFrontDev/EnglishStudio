using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using Xunit;

namespace EnglishStudio.Integration.Tests.Scoring;

public class BandScoreMapperTests
{
    private readonly BandScoreMapper _mapper = new();

    // Spot checks against the official Cambridge conversion tables, including the threshold edges.
    [Theory]
    [InlineData(40, 9.0)]
    [InlineData(39, 9.0)]
    [InlineData(38, 8.5)]
    [InlineData(30, 7.0)]
    [InlineData(29, 6.5)]
    [InlineData(23, 6.0)]
    [InlineData(22, 5.5)]
    [InlineData(1, 1.0)]
    [InlineData(0, 0.0)]
    public void AcademicReading_MatchesOfficialTable(int raw, double expected)
    {
        Assert.Equal(expected, _mapper.RawToBand(raw, IeltsSection.Reading, IeltsTestMode.Academic));
    }

    [Theory]
    [InlineData(40, 9.0)]
    [InlineData(39, 8.5)]
    [InlineData(34, 7.0)]
    [InlineData(30, 6.0)]
    [InlineData(23, 5.0)]
    [InlineData(0, 0.0)]
    public void GeneralTrainingReading_MatchesOfficialTable(int raw, double expected)
    {
        Assert.Equal(expected, _mapper.RawToBand(raw, IeltsSection.Reading, IeltsTestMode.GeneralTraining));
    }

    [Theory]
    [InlineData(39, 9.0)]
    [InlineData(35, 8.0)]
    [InlineData(30, 7.0)]
    [InlineData(26, 6.5)]
    [InlineData(23, 6.0)]
    [InlineData(16, 5.0)]
    [InlineData(0, 0.0)]
    public void Listening_MatchesOfficialTable(int raw, double expected)
    {
        Assert.Equal(expected, _mapper.RawToBand(raw, IeltsSection.Listening, IeltsTestMode.Academic));
    }

    [Theory]
    [InlineData(-5, 0.0)]
    [InlineData(45, 9.0)]
    public void RawScore_IsClampedTo0To40(int raw, double expected)
    {
        Assert.Equal(expected, _mapper.RawToBand(raw, IeltsSection.Listening, IeltsTestMode.Academic));
    }

    [Fact]
    public void UnsupportedSection_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => _mapper.RawToBand(10, IeltsSection.Writing, IeltsTestMode.Academic));
    }
}
