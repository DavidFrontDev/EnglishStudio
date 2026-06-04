using System.Globalization;
using System.Text;

namespace EnglishStudio.App.Audio;

public enum PronunciationCategory
{
    Excellent,
    Good,
    Poor,
    Unrecognized,
}

public sealed record PronunciationResult(
    string Target,
    string Recognized,
    int Score,            // 0..100
    PronunciationCategory Category,
    string FeedbackRu);

public sealed class PronunciationAssessor
{
    public PronunciationResult Assess(string target, string? recognized)
    {
        var t = Normalize(target);
        var r = Normalize(recognized ?? string.Empty);

        if (string.IsNullOrWhiteSpace(r))
        {
            return new PronunciationResult(target, recognized ?? "", 0, PronunciationCategory.Unrecognized,
                "Не удалось распознать речь. Попробуйте ещё раз.");
        }

        var distance = Levenshtein(t, r);
        var maxLen = Math.Max(t.Length, r.Length);
        var score = maxLen == 0 ? 0 : (int)Math.Round(100.0 * (1.0 - (double)distance / maxLen));
        score = Math.Clamp(score, 0, 100);

        var (category, feedback) = score switch
        {
            >= 90 => (PronunciationCategory.Excellent, "Отлично! Произношение близко к эталону."),
            >= 70 => (PronunciationCategory.Good, "Хорошо, но есть отличия. Послушайте эталон и попробуйте ещё."),
            _     => (PronunciationCategory.Poor, "Произношение сильно отличается. Послушайте эталон, повторите."),
        };

        return new PronunciationResult(target, recognized ?? "", score, category, feedback);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (char.IsWhiteSpace(ch) && sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }
        return sb.ToString().Trim();
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
