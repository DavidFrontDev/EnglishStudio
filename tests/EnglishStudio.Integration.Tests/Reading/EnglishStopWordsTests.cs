using EnglishStudio.Modules.Reading.Seed;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class EnglishStopWordsTests
{
    [Theory]
    [InlineData("the")]
    [InlineData("The")]   // case-insensitive
    [InlineData("and")]
    [InlineData("is")]
    [InlineData("their")]
    [InlineData("would")]
    public void Common_function_words_are_stop_words(string word) =>
        Assert.True(EnglishStopWords.IsStopWord(word));

    [Theory]
    [InlineData("philosophy")]
    [InlineData("entanglement")]
    [InlineData("cat")]
    public void Content_words_are_not_stop_words(string word) =>
        Assert.False(EnglishStopWords.IsStopWord(word));
}
