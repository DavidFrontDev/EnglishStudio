using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class SentenceSplitterTests
{
    private static IReadOnlyList<ShadowingSentence> Split(string text) =>
        new SentenceSplitter().Split(ReadingTokenizer.Tokenize(text));

    [Fact]
    public void Splits_on_terminal_punctuation()
    {
        var s = Split("The cat sat. A dog ran! Did it?");
        Assert.Equal(3, s.Count);
        Assert.Equal("The cat sat.", s[0].Text);
        Assert.Equal("A dog ran!", s[1].Text);
        Assert.Equal("Did it?", s[2].Text);
    }

    [Fact]
    public void Tracks_word_index_ranges()
    {
        // Words: The(0) cat(1) sat(2) | A(3) dog(4) ran(5)
        var s = Split("The cat sat. A dog ran!");
        Assert.Equal(0, s[0].StartWordIndex);
        Assert.Equal(2, s[0].EndWordIndex);
        Assert.Equal(3, s[1].StartWordIndex);
        Assert.Equal(5, s[1].EndWordIndex);
    }

    [Fact]
    public void Indexes_sentences_sequentially()
    {
        var s = Split("One. Two. Three.");
        Assert.Equal(new[] { 0, 1, 2 }, s.Select(x => x.Index).ToArray());
    }

    [Fact]
    public void Flushes_trailing_sentence_without_terminator()
    {
        var s = Split("First sentence. A trailing clause");
        Assert.Equal(2, s.Count);
        Assert.Equal("A trailing clause", s[1].Text);
    }

    [Fact]
    public void Collapses_newlines_and_spaces()
    {
        var s = Split("Hello   world\n\nagain here.");
        var single = Assert.Single(s);
        Assert.Equal("Hello world again here.", single.Text);
    }

    [Fact]
    public void Empty_text_yields_no_sentences()
    {
        Assert.Empty(Split(""));
        Assert.Empty(Split("   \n  "));
    }
}
