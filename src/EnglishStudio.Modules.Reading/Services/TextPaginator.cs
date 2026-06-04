using System.Text;
using System.Text.RegularExpressions;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Pure paginator for the reader (F8). Splits tokens into pages either by detected chapters or, within
/// each chapter / chapter-less text, by a target word count — cutting only at sentence ends or paragraph
/// breaks, never mid-sentence. All page ranges are in GLOBAL coordinates (<see cref="TextToken.WordIndex"/>
/// space and character offsets), so notes / bookmarks / read-along keep mapping across pages. No
/// dependencies → fully unit-testable. Registered as a singleton in <c>AddReadingStudyModule()</c>.
/// </summary>
public sealed partial class TextPaginator : IPaginationService
{
    public IReadOnlyList<TextPage> Paginate(IReadOnlyList<TextToken> tokens, PaginationOptions? options = null)
    {
        options ??= new PaginationOptions();
        var target = Math.Max(1, options.TargetWordsPerPage);

        if (tokens is null || tokens.Count == 0) return Array.Empty<TextPage>();
        // Nothing readable (whitespace/punctuation only) → no pages.
        if (!HasAnyWord(tokens)) return Array.Empty<TextPage>();

        var lines = BuildLines(tokens);
        var regions = BuildRegions(tokens, lines, options.DetectChapters);

        // Build a contiguous partition of the token list into page segments (token ranges).
        var segments = new List<PageSeg>();
        foreach (var region in regions)
            SplitRegion(tokens, region, target, segments);

        MergeWordlessSegments(segments);

        // Project segments to global-coordinate pages.
        var pages = new List<TextPage>(segments.Count);
        for (var idx = 0; idx < segments.Count; idx++)
        {
            var s = segments[idx];
            var (startWord, endWord) = WordRange(tokens, s.Start, s.EndExclusive);
            var startChar = tokens[s.Start].StartOffset;
            var lastTok = tokens[s.EndExclusive - 1];
            var endChar = lastTok.StartOffset + lastTok.Length;
            pages.Add(new TextPage(idx, startWord, endWord, startChar, endChar, s.Heading));
        }
        return pages;
    }

    public int PageOfWord(IReadOnlyList<TextPage> pages, int wordIndex)
    {
        if (pages is null || pages.Count == 0) return -1;
        int lo = 0, hi = pages.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var p = pages[mid];
            if (wordIndex < p.StartWordIndex) hi = mid - 1;
            else if (wordIndex > p.EndWordIndex) lo = mid + 1;
            else return mid;
        }
        return -1;
    }

    // ───────────────────────── splitting ─────────────────────────

    /// <summary>Splits one region into pages. A region ≤ target → exactly one page (keeps small-text UX).</summary>
    private static void SplitRegion(IReadOnlyList<TextToken> tokens, Region region, int target, List<PageSeg> output)
    {
        var regionWords = CountWords(tokens, region.Start, region.EndExclusive);
        if (regionWords <= target)
        {
            output.Add(new PageSeg(region.Start, region.EndExclusive, region.Heading, regionWords));
            return;
        }

        var pageStart = region.Start;
        var wordsInPage = 0;
        var firstPage = true;
        var hardCap = target * 2;   // run-on text without sentence ends: cut at a word boundary instead

        for (var i = region.Start; i < region.EndExclusive; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Word) wordsInPage++;

            var sentenceEnd = IsSentenceEnd(t);
            var forced = wordsInPage >= hardCap && (t.Kind == TokenKind.Space || t.Kind == TokenKind.Break);

            if (wordsInPage >= target && (sentenceEnd || forced))
            {
                output.Add(new PageSeg(pageStart, i + 1, firstPage ? region.Heading : null, wordsInPage));
                firstPage = false;
                pageStart = i + 1;
                wordsInPage = 0;
            }
        }

        if (pageStart < region.EndExclusive)
            output.Add(new PageSeg(pageStart, region.EndExclusive, firstPage ? region.Heading : null, wordsInPage));
    }

    /// <summary>Folds any word-less trailing/leading segment into a neighbour so every page has ≥1 word
    /// while keeping the token partition contiguous (no gaps / overlap).</summary>
    private static void MergeWordlessSegments(List<PageSeg> segs)
    {
        for (var i = 0; i < segs.Count; i++)
        {
            if (segs[i].WordCount > 0) continue;
            if (i > 0)
            {
                segs[i - 1] = segs[i - 1] with { EndExclusive = segs[i].EndExclusive };
                segs.RemoveAt(i);
                i--;
            }
            else if (segs.Count > 1)
            {
                segs[i + 1] = segs[i + 1] with { Start = segs[i].Start, Heading = segs[i].Heading ?? segs[i + 1].Heading };
                segs.RemoveAt(i);
                i--;
            }
            // else: the only segment and it has no words — left as-is (Paginate already guards no-word input).
        }
    }

    // ───────────────────────── chapters / regions ─────────────────────────

    private static List<Region> BuildRegions(IReadOnlyList<TextToken> tokens, List<Line> lines, bool detectChapters)
    {
        var regions = new List<Region>();

        if (detectChapters)
        {
            var headings = new List<(int Start, string Text)>();
            for (var li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                if (!line.HasWord) continue;

                var text = CleanHeading(LineText(tokens, line));
                if (text.Length == 0 || line.WordCount > 8) continue;   // headings are short lines

                var isHeading = ChapterRegex().IsMatch(text);
                if (!isHeading)
                {
                    // Secondary, conservative: an ALL-CAPS line isolated by blank lines (e.g. "WAR AND PEACE").
                    var prevBlank = li == 0 || !lines[li - 1].HasWord;
                    var nextBlank = li == lines.Count - 1 || !lines[li + 1].HasWord;
                    isHeading = prevBlank && nextBlank && IsAllCaps(text);
                }

                if (isHeading) headings.Add((line.Start, text));
            }

            if (headings.Count > 0)
            {
                if (headings[0].Start > 0)
                    regions.Add(new Region(0, headings[0].Start, null));   // front matter before first chapter
                for (var k = 0; k < headings.Count; k++)
                {
                    var end = k + 1 < headings.Count ? headings[k + 1].Start : tokens.Count;
                    regions.Add(new Region(headings[k].Start, end, headings[k].Text));
                }
                return regions;
            }
        }

        regions.Add(new Region(0, tokens.Count, null));
        return regions;
    }

    /// <summary>Splits tokens into lines on <see cref="TokenKind.Break"/> (the break itself joins neither side).</summary>
    private static List<Line> BuildLines(IReadOnlyList<TextToken> tokens)
    {
        var lines = new List<Line>();
        var start = 0;
        var hasWord = false;
        var wordCount = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind == TokenKind.Break)
            {
                lines.Add(new Line(start, i, hasWord, wordCount));
                start = i + 1;
                hasWord = false;
                wordCount = 0;
            }
            else if (tokens[i].Kind == TokenKind.Word)
            {
                hasWord = true;
                wordCount++;
            }
        }
        lines.Add(new Line(start, tokens.Count, hasWord, wordCount));
        return lines;
    }

    private static string LineText(IReadOnlyList<TextToken> tokens, Line line)
    {
        var sb = new StringBuilder();
        for (var i = line.Start; i < line.EndExclusive; i++) sb.Append(tokens[i].Text);
        return sb.ToString();
    }

    private static string CleanHeading(string raw) => WhitespaceRegex().Replace(raw, " ").Trim();

    private static bool IsAllCaps(string text)
    {
        if (text.Length > 60) return false;
        var hasLetter = false;
        foreach (var c in text)
        {
            if (!char.IsLetter(c)) continue;
            hasLetter = true;
            if (!char.IsUpper(c)) return false;
        }
        return hasLetter;
    }

    // ───────────────────────── token helpers ─────────────────────────

    private static bool HasAnyWord(IReadOnlyList<TextToken> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
            if (tokens[i].Kind == TokenKind.Word) return true;
        return false;
    }

    private static int CountWords(IReadOnlyList<TextToken> tokens, int start, int endExclusive)
    {
        var n = 0;
        for (var i = start; i < endExclusive; i++)
            if (tokens[i].Kind == TokenKind.Word) n++;
        return n;
    }

    private static (int Start, int End) WordRange(IReadOnlyList<TextToken> tokens, int start, int endExclusive)
    {
        var first = -1;
        var last = -1;
        for (var i = start; i < endExclusive; i++)
        {
            if (tokens[i].Kind != TokenKind.Word || tokens[i].WordIndex is not int w) continue;
            if (first < 0) first = w;
            last = w;
        }
        return (first, last);
    }

    private static bool IsSentenceEnd(TextToken t) =>
        t.Kind == TokenKind.Punctuation && t.Text.Length == 1 && (t.Text[0] == '.' || t.Text[0] == '!' || t.Text[0] == '?');

    [GeneratedRegex(@"^(chapter|part|book|section|prologue|epilogue|глава|часть|книга|пролог|эпилог)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChapterRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private readonly record struct Line(int Start, int EndExclusive, bool HasWord, int WordCount);
    private readonly record struct Region(int Start, int EndExclusive, string? Heading);
    private readonly record struct PageSeg(int Start, int EndExclusive, string? Heading, int WordCount);
}
