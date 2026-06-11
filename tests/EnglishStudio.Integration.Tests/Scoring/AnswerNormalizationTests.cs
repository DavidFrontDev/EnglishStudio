using EnglishStudio.Modules.Ielts.Core.Scoring;
using Xunit;

namespace EnglishStudio.Integration.Tests.Scoring;

public class AnswerNormalizationTests
{
    [Theory]
    [InlineData("1,000 kg", "1000 kg")]        // thousands separator collapses (regression)
    [InlineData("1,000,000", "1000000")]
    [InlineData("3,5", "3.5")]                 // decimal comma → decimal point (regression)
    [InlineData("23,70", "23.70")]
    [InlineData("23.70", "23.70")]             // decimal point survives between digits
    [InlineData("$23.70", "23.70")]
    [InlineData("Hello, world!", "hello world")]
    [InlineData("  The   Answer.  ", "the answer")]
    [InlineData("well-known", "well-known")]   // hyphen kept
    [InlineData("isn't", "isn't")]             // apostrophe kept
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalize_HandlesNumbersPunctuationAndCase(string input, string expected)
    {
        Assert.Equal(expected, AnswerNormalization.Normalize(input));
    }

    [Theory]
    [InlineData("1,000 kg", 2)]        // a digit-grouped number is ONE word (regression: was 3)
    [InlineData("23.70", 1)]
    [InlineData("3,5", 1)]
    [InlineData("well-known author", 2)]
    [InlineData("the architect's name", 3)]
    [InlineData("", 0)]
    [InlineData("one two three", 3)]
    public void CountWords_FollowsIeltsRules(string input, int expected)
    {
        Assert.Equal(expected, AnswerNormalization.CountWords(input));
    }

    [Theory]
    [InlineData("1,000", "1000", true)]
    [InlineData("3,5", "3.5", true)]
    [InlineData("Twenty", "20", true)]    // number↔word equivalence 0–20
    [InlineData("7", "seven", true)]
    [InlineData("answer", "ANSWER", true)]
    [InlineData("answer.", "answer", true)]
    [InlineData("cat", "dog", false)]
    [InlineData("", "answer", false)]
    [InlineData("answer", "", false)]
    public void Equivalent_ComparesLeniently(string a, string b, bool expected)
    {
        Assert.Equal(expected, AnswerNormalization.Equivalent(a, b));
    }
}
