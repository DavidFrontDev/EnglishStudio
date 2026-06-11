using System.Text;
using System.Text.RegularExpressions;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// IELTS answer normalization helpers — implements the lenient comparison rules used by the official
/// answer keys (case-insensitive, whitespace-tolerant, trailing punctuation stripped, common
/// number↔word equivalence, hyphens treated as single word).
/// </summary>
public static class AnswerNormalization
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex TrailingPunctuation = new(@"[\.\!\?\,\;\:]+$", RegexOptions.Compiled);
    // A comma between digits is a thousands separator when exactly three digits follow
    // ("1,000" → "1000"); otherwise treat it as a decimal comma ("3,5" → "3.5").
    private static readonly Regex ThousandsSeparator = new(@"(?<=\d),(?=\d{3}(?!\d))", RegexOptions.Compiled);
    private static readonly Regex DecimalComma = new(@"(?<=\d),(?=\d)", RegexOptions.Compiled);
    private static readonly Regex AllowedChars = new(@"[^\p{L}\p{N}\s\-'.]|(?<!\d)\.|\.(?!\d)", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var s = input.Trim().ToLowerInvariant();
        s = ThousandsSeparator.Replace(s, string.Empty);
        s = DecimalComma.Replace(s, ".");
        s = AllowedChars.Replace(s, " ");
        s = WhitespaceRun.Replace(s, " ").Trim();
        s = TrailingPunctuation.Replace(s, string.Empty);
        return s;
    }

    /// <summary>
    /// Count words by IELTS rule: hyphenated tokens count as one word, contractions count as one,
    /// numbers count as one.
    /// </summary>
    public static int CountWords(string input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrEmpty(normalized)) return 0;
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    /// <summary>
    /// Compare two answers leniently. Returns true if they are considered equivalent.
    /// </summary>
    public static bool Equivalent(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;

        // Number↔word for the common 0–20 range (sufficient for most NMTW answers).
        if (NumberWordEquivalent(na, nb)) return true;

        return false;
    }

    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
    {
        ["0"] = "zero", ["1"] = "one", ["2"] = "two", ["3"] = "three", ["4"] = "four",
        ["5"] = "five", ["6"] = "six", ["7"] = "seven", ["8"] = "eight", ["9"] = "nine",
        ["10"] = "ten", ["11"] = "eleven", ["12"] = "twelve", ["13"] = "thirteen",
        ["14"] = "fourteen", ["15"] = "fifteen", ["16"] = "sixteen", ["17"] = "seventeen",
        ["18"] = "eighteen", ["19"] = "nineteen", ["20"] = "twenty"
    };

    private static bool NumberWordEquivalent(string a, string b)
    {
        if (NumberWords.TryGetValue(a, out var wa) && wa == b) return true;
        if (NumberWords.TryGetValue(b, out var wb) && wb == a) return true;
        return false;
    }

    /// <summary>
    /// Compute a 2-char fingerprint used to log near-misses; not used for scoring.
    /// </summary>
    public static string Fingerprint(string s)
    {
        var sb = new StringBuilder(8);
        var n = Normalize(s);
        foreach (var ch in n)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            if (sb.Length >= 8) break;
        }
        return sb.ToString();
    }
}
