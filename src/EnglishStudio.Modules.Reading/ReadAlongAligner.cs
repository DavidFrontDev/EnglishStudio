namespace EnglishStudio.Modules.Reading;

/// <summary>
/// Forced-alignment cursor over a known reference text. Feed it the words an ASR
/// engine recognized (in reading order) and it advances a forward-only cursor through
/// the reference — tolerating skips, repeats and mis-recognitions.
/// </summary>
/// <remarks>
/// Pure and dependency-free so it can be unit-tested without audio or Vosk.
/// The reference words must be normalized the same way the fed words are
/// (see <see cref="ReadingTokenizer.NormalizeWord"/>). The cursor index is in the same
/// 0-based word-index space as <see cref="TextToken.WordIndex"/>, so the UI can dim every
/// word whose index is below the cursor.
/// </remarks>
public sealed class ReadAlongAligner
{
    private readonly IReadOnlyList<string> _reference;
    private readonly int _lookahead;

    /// <param name="referenceWordsNormalized">
    /// Normalized reference words in reading order; element <c>i</c> corresponds to the
    /// word whose <see cref="TextToken.WordIndex"/> is <c>i</c>.
    /// </param>
    /// <param name="lookahead">
    /// How far ahead of the cursor a recognized word may match. Larger tolerates bigger
    /// skips but risks jumping ahead on repeated common words.
    /// </param>
    public ReadAlongAligner(IReadOnlyList<string> referenceWordsNormalized, int lookahead = 10)
    {
        _reference = referenceWordsNormalized ?? throw new ArgumentNullException(nameof(referenceWordsNormalized));
        _lookahead = Math.Max(1, lookahead);
    }

    /// <summary>Index of the next expected reference word (== words already read).</summary>
    public int Cursor { get; private set; }

    /// <summary>Total reference words.</summary>
    public int Total => _reference.Count;

    /// <summary>True once the cursor has reached the end of the reference.</summary>
    public bool IsComplete => Cursor >= _reference.Count;

    /// <summary>
    /// Advances the cursor over <paramref name="recognizedNormalized"/> (already normalized,
    /// in reading order). The cursor only ever moves forward. Returns the new cursor.
    /// </summary>
    public int Accept(IReadOnlyList<string> recognizedNormalized)
    {
        if (recognizedNormalized is null) return Cursor;

        foreach (var word in recognizedNormalized)
        {
            if (Cursor >= _reference.Count) break;
            if (string.IsNullOrEmpty(word)) continue;

            // Nearest match in the forward window [Cursor .. Cursor+lookahead]. Taking the
            // closest (not farthest) match avoids skipping ahead on a repeated word like "the".
            var limit = Math.Min(_reference.Count, Cursor + _lookahead + 1);
            for (var i = Cursor; i < limit; i++)
            {
                if (IsMatch(_reference[i], word))
                {
                    Cursor = i + 1;
                    break;
                }
            }
        }

        return Cursor;
    }

    /// <summary>
    /// Fuzzy word match: exact, a long-enough prefix relationship, or an edit distance of 1
    /// for longer words. Conservative on purpose — false positives jump the cursor ahead.
    /// </summary>
    internal static bool IsMatch(string reference, string recognized)
    {
        if (reference == recognized) return true;
        if (reference.Length == 0 || recognized.Length == 0) return false;

        var min = Math.Min(reference.Length, recognized.Length);
        if (min >= 4 && (reference.StartsWith(recognized, StringComparison.Ordinal)
                         || recognized.StartsWith(reference, StringComparison.Ordinal)))
            return true;

        // Edit-distance tolerance scaled to length (≈25%): 1 for 5–7 chars, 2 for 8–11, etc.
        // Short words stay exact-only so common words don't jump the cursor ahead.
        var max = Math.Max(reference.Length, recognized.Length);
        if (max >= 5 && Levenshtein(reference, recognized) <= Math.Max(1, max / 4))
            return true;

        return false;
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }
}
