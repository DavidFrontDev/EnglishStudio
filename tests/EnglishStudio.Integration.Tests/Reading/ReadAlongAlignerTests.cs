using EnglishStudio.Modules.Reading;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>
/// Pure tests for the forced-alignment cursor: it only moves forward, tolerates skips,
/// repeats and mis-recognitions, and never jumps backwards on jitter.
/// </summary>
public class ReadAlongAlignerTests
{
    private static ReadAlongAligner Make(string reference, int lookahead = 10) =>
        new(reference.Split(' ', StringSplitOptions.RemoveEmptyEntries), lookahead);

    private static string[] Words(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Starts_at_zero()
    {
        var a = Make("the cat sat on the mat");
        Assert.Equal(0, a.Cursor);
        Assert.Equal(6, a.Total);
        Assert.False(a.IsComplete);
    }

    [Fact]
    public void Advances_word_by_word_on_exact_reads()
    {
        var a = Make("the cat sat on the mat");
        Assert.Equal(1, a.Accept(Words("the")));
        Assert.Equal(2, a.Accept(Words("cat")));
        Assert.Equal(3, a.Accept(Words("sat")));
    }

    [Fact]
    public void Accepts_a_whole_chunk_at_once()
    {
        var a = Make("the cat sat on the mat");
        Assert.Equal(4, a.Accept(Words("the cat sat on")));
    }

    [Fact]
    public void Tolerates_a_skipped_word()
    {
        var a = Make("the quick brown fox jumps");
        // Reader skips "quick".
        a.Accept(Words("the"));
        Assert.Equal(3, a.Accept(Words("brown")));
    }

    [Fact]
    public void Never_moves_backwards_on_a_repeated_word()
    {
        var a = Make("the cat sat on the mat");
        a.Accept(Words("the cat sat on"));
        var before = a.Cursor;
        // Vosk re-emits an earlier "the" — must not rewind past the closer later "the" at index 4.
        var after = a.Accept(Words("the"));
        Assert.True(after >= before);
        Assert.Equal(5, after);
    }

    [Fact]
    public void Picks_nearest_match_not_farthest_for_common_word()
    {
        var a = Make("the dog and the cat");
        // First recognized "the" should land at index 1, not jump to the second "the" (index 4).
        Assert.Equal(1, a.Accept(Words("the")));
    }

    [Fact]
    public void Tolerates_misrecognition_via_edit_distance()
    {
        var a = Make("elephant walked slowly");
        // "elephant" misheard as "elefant" (edit distance 1, long word).
        Assert.Equal(1, a.Accept(Words("elefant")));
    }

    [Fact]
    public void Tolerates_prefix_match()
    {
        var a = Make("information overload");
        // "informations" recognized — prefix relationship on a long word.
        Assert.Equal(1, a.Accept(Words("informations")));
    }

    [Fact]
    public void Does_not_match_unrelated_short_words()
    {
        var a = Make("a to be or not");
        // "xy" matches nothing in the window; cursor stays put.
        Assert.Equal(0, a.Accept(Words("xy")));
    }

    [Fact]
    public void Lookahead_bounds_forward_jumps()
    {
        var a = Make("one two three four five six seven eight nine ten target", lookahead: 3);
        // "target" is far beyond the window from cursor 0 — should not jump there.
        Assert.Equal(0, a.Accept(Words("target")));
    }

    [Fact]
    public void Reaches_completion_at_end_of_text()
    {
        var a = Make("read this short line");
        a.Accept(Words("read this short line"));
        Assert.True(a.IsComplete);
        Assert.Equal(a.Total, a.Cursor);
    }

    [Fact]
    public void Does_not_overshoot_past_end()
    {
        var a = Make("two words");
        Assert.Equal(2, a.Accept(Words("two words and then some extra noise")));
        Assert.Equal(2, a.Cursor);
    }

    [Fact]
    public void Empty_reference_is_immediately_complete()
    {
        var a = Make("");
        Assert.Equal(0, a.Total);
        Assert.True(a.IsComplete);
        Assert.Equal(0, a.Accept(Words("anything at all")));
    }

    [Fact]
    public void Ignores_empty_and_null_input()
    {
        var a = Make("hello world");
        Assert.Equal(0, a.Accept(Array.Empty<string>()));
        Assert.Equal(0, a.Accept(null!));
        Assert.Equal(1, a.Accept(Words("hello")));
    }
}
