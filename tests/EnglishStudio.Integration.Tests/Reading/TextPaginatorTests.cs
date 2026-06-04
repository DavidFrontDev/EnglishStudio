using System.Text;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>
/// Unit tests for the pure F8 paginator (Agent A): chapter detection, size-based splitting at sentence
/// boundaries, single-page small texts, contiguous coverage and <c>PageOfWord</c>.
/// </summary>
public class TextPaginatorTests
{
    private static readonly TextPaginator Paginator = new();

    private static IReadOnlyList<TextPage> Paginate(string text, int target = 1500, bool chapters = true)
        => Paginator.Paginate(ReadingTokenizer.Tokenize(text), new PaginationOptions(target, chapters));

    private static string Sentences(int count, string sentence = "alpha beta gamma delta epsilon.")
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++) sb.Append(sentence).Append(' ');
        return sb.ToString().TrimEnd();
    }

    private static void AssertContiguous(IReadOnlyList<TextPage> pages, string text)
    {
        Assert.Equal(0, pages[0].StartWordIndex);
        Assert.Equal(0, pages[0].StartCharOffset);
        Assert.Equal(text.Length, pages[^1].EndCharOffset);
        for (var i = 0; i < pages.Count - 1; i++)
        {
            Assert.Equal(pages[i].EndWordIndex + 1, pages[i + 1].StartWordIndex);   // no word gap/overlap
            Assert.Equal(pages[i].EndCharOffset, pages[i + 1].StartCharOffset);      // no char gap/overlap
            Assert.True(pages[i].StartWordIndex <= pages[i].EndWordIndex);           // each page has ≥1 word
        }
    }

    [Fact]
    public void SmallText_WithoutChapters_YieldsSinglePage()
    {
        const string text = "Hello world. This is short.";
        var pages = Paginate(text);

        var p = Assert.Single(pages);
        Assert.Equal(0, p.StartWordIndex);
        Assert.Equal(4, p.EndWordIndex);           // 5 words: Hello world This is short
        Assert.Null(p.Heading);
        Assert.Equal(0, p.StartCharOffset);
        Assert.Equal(text.Length, p.EndCharOffset);
    }

    [Fact]
    public void NoChapters_SplitsIntoEvenPagesAtSentenceBoundaries()
    {
        var text = Sentences(100);             // 100 × 5 words = 500 words, every sentence ends with '.'
        var pages = Paginate(text, target: 20, chapters: false);

        Assert.True(pages.Count > 1);
        AssertContiguous(pages, text);
        Assert.Equal(0, pages[0].StartWordIndex);
        Assert.Equal(499, pages[^1].EndWordIndex);

        for (var i = 0; i < pages.Count - 1; i++)
        {
            // page boundary lands right after a sentence terminator → never mid-sentence
            var consumed = text[..pages[i + 1].StartCharOffset].TrimEnd();
            Assert.EndsWith(".", consumed);

            // each completed page holds at least the target word count
            var words = pages[i].EndWordIndex - pages[i].StartWordIndex + 1;
            Assert.True(words >= 20, $"page {i} had {words} words");
        }
    }

    [Fact]
    public void Chapters_ProduceOnePagePerChapterWithHeading()
    {
        const string text =
            "Chapter 1\n\nFirst chapter body here.\n\nChapter 2\n\nSecond chapter body here.";
        var pages = Paginate(text, target: 1500, chapters: true);

        Assert.Equal(2, pages.Count);
        Assert.Equal("Chapter 1", pages[0].Heading);
        Assert.Equal("Chapter 2", pages[1].Heading);
        AssertContiguous(pages, text);
    }

    [Fact]
    public void LongChapter_SubSplits_HeadingOnlyOnFirstPage()
    {
        var text = "Chapter 1\n\n" + Sentences(100);   // one chapter, 500 words
        var pages = Paginate(text, target: 20, chapters: true);

        Assert.True(pages.Count > 1);
        Assert.Equal("Chapter 1", pages[0].Heading);
        Assert.All(pages.Skip(1), p => Assert.Null(p.Heading));   // sub-pages carry no heading
        AssertContiguous(pages, text);
    }

    [Fact]
    public void PageOfWord_IsCorrectOnBoundariesAndOutOfRange()
    {
        var text = "Chapter 1\n\n" + Sentences(100);
        var pages = Paginate(text, target: 20, chapters: true);

        foreach (var p in pages)
        {
            Assert.Equal(p.Index, Paginator.PageOfWord(pages, p.StartWordIndex));
            Assert.Equal(p.Index, Paginator.PageOfWord(pages, p.EndWordIndex));
        }
        Assert.Equal(-1, Paginator.PageOfWord(pages, -1));
        Assert.Equal(-1, Paginator.PageOfWord(pages, 100_000));
    }

    [Fact]
    public void ParagraphStartingWithKeyword_IsNotMistakenForChapter()
    {
        // A long line beginning with "Part …" must NOT be detected as a chapter heading.
        const string text = "Part of the reason this experiment failed was clear to everyone involved.";
        var pages = Paginate(text, target: 1500, chapters: true);

        var p = Assert.Single(pages);
        Assert.Null(p.Heading);
    }

    [Fact]
    public void EmptyOrWordlessInput_YieldsNoPages()
    {
        Assert.Empty(Paginate(string.Empty));
        Assert.Empty(Paginate("   \n\n   "));
    }
}
