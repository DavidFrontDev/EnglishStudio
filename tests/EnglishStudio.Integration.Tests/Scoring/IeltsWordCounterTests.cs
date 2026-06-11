using EnglishStudio.Modules.Ielts.Writing;
using Xunit;

namespace EnglishStudio.Integration.Tests.Scoring;

public class IeltsWordCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("One two three.", 3)]
    [InlineData("1,500", 1)]              // regression: comma-grouped number is ONE word
    [InlineData("1.5", 1)]
    [InlineData("well-known", 1)]         // hyphenated counts as one
    [InlineData("isn't", 1)]              // contraction counts as one
    [InlineData("The chart shows 1,500 users.", 5)]
    [InlineData("First, we analyse. Second, we conclude.", 6)]
    public void Count_FollowsIeltsConventions(string? text, int expected)
    {
        Assert.Equal(expected, IeltsWordCounter.Count(text));
    }
}
