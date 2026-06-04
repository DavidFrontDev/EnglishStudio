using System.Text;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Phoneme-level pronunciation feedback (F3). <see cref="BuildGuide"/> is reliable (lexicon only);
/// <see cref="Compare"/> is best-effort (Whisper gives words, not phonemes, so the intended-vs-said
/// phoneme diff is an approximation surfaced as a hint). Phoneme strings in the output are IPA.
/// </summary>
public sealed class PhonemeFeedbackService : IPhonemeFeedbackService
{
    private const int MaxFeedbackItems = 4;

    private readonly IPronunciationLexicon _lexicon;

    public PhonemeFeedbackService(IPronunciationLexicon lexicon) => _lexicon = lexicon;

    public WordPronunciationGuide BuildGuide(string word)
    {
        var clean = (word ?? string.Empty).Trim();
        if (!_lexicon.TryGetArpabet(clean, out var phonemes) || phonemes.Count == 0)
            return new WordPronunciationGuide(clean, string.Empty, Array.Empty<PhonemeUnit>(), Found: false);

        var units = new List<PhonemeUnit>(phonemes.Count);
        foreach (var p in phonemes)
            units.Add(new PhonemeUnit(p, Ipa(p), _lexicon.IsTrickyForRu(p)));

        return new WordPronunciationGuide(clean, _lexicon.ToIpa(phonemes), units, Found: true);
    }

    public WordPhonemeFeedback Compare(string referenceWord, string recognizedWord)
    {
        var reference = (referenceWord ?? string.Empty).Trim();
        var recognized = (recognizedWord ?? string.Empty).Trim();

        if (!_lexicon.TryGetArpabet(reference, out var refPhonemes) || refPhonemes.Count == 0 ||
            !_lexicon.TryGetArpabet(recognized, out var saidPhonemes) || saidPhonemes.Count == 0)
        {
            return new WordPhonemeFeedback(reference, recognized, Array.Empty<PhonemeDiff>(),
                "Нет данных о произношении.", HasData: false);
        }

        var diffs = AlignPhonemes(refPhonemes, saidPhonemes);
        return new WordPhonemeFeedback(reference, recognized, diffs, BuildFeedbackRu(diffs), HasData: true);
    }

    /// <summary>Needleman-Wunsch over phonemes (match 0, sub/indel 1) → ordered diffs (IPA glyphs).</summary>
    private List<PhonemeDiff> AlignPhonemes(IReadOnlyList<string> reference, IReadOnlyList<string> said)
    {
        var m = reference.Count;
        var n = said.Count;
        var dp = new int[m + 1, n + 1];
        var back = new byte[m + 1, n + 1]; // 0=diag 1=up(deletion) 2=left(insertion)

        for (var i = 1; i <= m; i++) { dp[i, 0] = i; back[i, 0] = 1; }
        for (var j = 1; j <= n; j++) { dp[0, j] = j; back[0, j] = 2; }

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var same = string.Equals(reference[i - 1], said[j - 1], StringComparison.Ordinal);
                var diag = dp[i - 1, j - 1] + (same ? 0 : 1);
                var up = dp[i - 1, j] + 1;
                var left = dp[i, j - 1] + 1;

                if (diag <= up && diag <= left) { dp[i, j] = diag; back[i, j] = 0; }
                else if (up <= left) { dp[i, j] = up; back[i, j] = 1; }
                else { dp[i, j] = left; back[i, j] = 2; }
            }
        }

        var reversed = new List<PhonemeDiff>();
        int ri = m, sj = n;
        while (ri > 0 || sj > 0)
        {
            var dir = (ri > 0 && sj > 0) ? back[ri, sj] : (ri > 0 ? (byte)1 : (byte)2);
            if (dir == 0)
            {
                var r = reference[ri - 1];
                var s = said[sj - 1];
                var kind = string.Equals(r, s, StringComparison.Ordinal) ? PhonemeDiffKind.Match : PhonemeDiffKind.Substitution;
                reversed.Add(new PhonemeDiff(kind, Ipa(r), Ipa(s)));
                ri--; sj--;
            }
            else if (dir == 1)
            {
                reversed.Add(new PhonemeDiff(PhonemeDiffKind.Deletion, Ipa(reference[ri - 1]), null));
                ri--;
            }
            else
            {
                reversed.Add(new PhonemeDiff(PhonemeDiffKind.Insertion, null, Ipa(said[sj - 1])));
                sj--;
            }
        }

        reversed.Reverse();
        return reversed;
    }

    private static string BuildFeedbackRu(IReadOnlyList<PhonemeDiff> diffs)
    {
        var problems = new List<string>();
        foreach (var d in diffs)
        {
            if (problems.Count >= MaxFeedbackItems) break;
            switch (d.Kind)
            {
                case PhonemeDiffKind.Substitution:
                    problems.Add($"/{d.Reference}/ → /{d.Said}/");
                    break;
                case PhonemeDiffKind.Deletion:
                    problems.Add($"пропущен /{d.Reference}/");
                    break;
                case PhonemeDiffKind.Insertion:
                    problems.Add($"лишний /{d.Said}/");
                    break;
            }
        }

        return problems.Count == 0
            ? "Произношение точное."
            : "Различия: " + string.Join("; ", problems) + ".";
    }

    private string Ipa(string arpabet)
    {
        var s = _lexicon.ToIpa(new[] { arpabet });
        return string.IsNullOrEmpty(s) ? arpabet : s;
    }
}
