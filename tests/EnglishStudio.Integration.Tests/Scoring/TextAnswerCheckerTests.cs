using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using Xunit;

namespace EnglishStudio.Integration.Tests.Scoring;

public class TextAnswerCheckerTests
{
    private readonly TextAnswerChecker _checker = new();

    private static TestQuestion Question(
        string key,
        string? acceptable = null,
        int? wordLimit = null,
        int points = 1) => new()
    {
        Type = QuestionType.SentenceCompletion,
        AnswerKeyJson = key,
        AcceptableAnswersJson = acceptable,
        WordLimitMax = wordLimit,
        Points = points
    };

    [Fact]
    public void ExactMatch_IsCorrect()
    {
        var result = _checker.Check(Question("library"), "library");
        Assert.True(result.IsCorrect);
        Assert.Equal(1, result.PointsEarned);
    }

    [Fact]
    public void DigitGroupedKey_AcceptsBothForms()
    {
        // Regression: "1,000 kg" with a 2-word limit used to be unanswerable in any form.
        var q = Question("1,000 kg", wordLimit: 2);
        Assert.True(_checker.Check(q, "1,000 kg").IsCorrect);
        Assert.True(_checker.Check(q, "1000 kg").IsCorrect);
    }

    [Fact]
    public void AcceptableAnswer_MatchesEvenOverWordLimit()
    {
        // Regression: listed spelled-out alternatives were unreachable behind the NMTW check.
        var q = Question("239", acceptable: """["two hundred and thirty-nine"]""", wordLimit: 2);
        Assert.True(_checker.Check(q, "two hundred and thirty-nine").IsCorrect);
    }

    [Fact]
    public void OverLimitNonMatch_IsRejectedWithNote()
    {
        var q = Question("library", wordLimit: 2);
        var result = _checker.Check(q, "the big public library");
        Assert.False(result.IsCorrect);
        Assert.Equal(0, result.PointsEarned);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public void WrongAnswerWithinLimit_IsRejectedWithoutNote()
    {
        var q = Question("library", wordLimit: 2);
        var result = _checker.Check(q, "museum");
        Assert.False(result.IsCorrect);
        Assert.Null(result.Note);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyAnswer_IsRejected(string answer)
    {
        Assert.False(_checker.Check(Question("library"), answer).IsCorrect);
    }

    [Fact]
    public void JsonStringLiteralAnswer_IsUnwrapped()
    {
        Assert.True(_checker.Check(Question("\"library\""), "\"library\"").IsCorrect);
    }

    [Fact]
    public void CaseAndTrailingPunctuation_AreIgnored()
    {
        Assert.True(_checker.Check(Question("Library"), "  LIBRARY.  ").IsCorrect);
    }

    [Fact]
    public void NumberWordEquivalence_Works()
    {
        Assert.True(_checker.Check(Question("7"), "seven").IsCorrect);
    }
}
