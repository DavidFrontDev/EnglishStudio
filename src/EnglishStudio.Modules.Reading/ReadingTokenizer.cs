using System.Globalization;
using System.Text;

namespace EnglishStudio.Modules.Reading;

public enum TokenKind
{
    Word,
    Punctuation,
    Space,
    Break
}

/// <summary>
/// A single unit of a tokenized text. <see cref="WordIndex"/> is set only for
/// <see cref="TokenKind.Word"/> tokens (running 0-based count of words), enabling
/// per-word rendering, selection mapping and read-along cursor alignment.
/// </summary>
public sealed record TextToken(
    string Text,
    TokenKind Kind,
    int StartOffset,
    int Length,
    int? WordIndex);

/// <summary>
/// Splits raw text into word / punctuation / whitespace / line-break tokens with
/// character offsets. Pure and allocation-light; used by the reader (per-word Runs),
/// the CEFR estimator and the read-along tracker.
/// </summary>
public static class ReadingTokenizer
{
    /// <summary>
    /// Word count above which a text is treated as "large": the reader renders one Run per word
    /// in a single FlowDocument (not virtualized), so very large texts (whole books) can lag or
    /// hang on open. The library warns past this threshold; F8 (pagination) is the real fix.
    /// </summary>
    public const int LargeTextWordThreshold = 20_000;

    public static IReadOnlyList<TextToken> Tokenize(string text)
    {
        var tokens = new List<TextToken>();
        if (string.IsNullOrEmpty(text)) return tokens;

        var i = 0;
        var wordIndex = 0;
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];

            // Line breaks (collapse \r\n into one Break token).
            if (c == '\n' || c == '\r')
            {
                var start = i;
                if (c == '\r' && i + 1 < n && text[i + 1] == '\n') i += 2;
                else i += 1;
                tokens.Add(new TextToken(text.Substring(start, i - start), TokenKind.Break, start, i - start, null));
                continue;
            }

            // Other whitespace.
            if (char.IsWhiteSpace(c))
            {
                var start = i;
                while (i < n && char.IsWhiteSpace(text[i]) && text[i] != '\n' && text[i] != '\r') i++;
                tokens.Add(new TextToken(text.Substring(start, i - start), TokenKind.Space, start, i - start, null));
                continue;
            }

            // Word: letters/digits, with internal apostrophes and hyphens (don't / well-known).
            if (IsWordChar(c))
            {
                var start = i;
                i++;
                while (i < n)
                {
                    if (IsWordChar(text[i])) { i++; continue; }
                    // Keep an apostrophe/hyphen only if it sits between two word chars.
                    if ((text[i] == '\'' || text[i] == '’' || text[i] == '-')
                        && i + 1 < n && IsWordChar(text[i + 1]))
                    {
                        i++;
                        continue;
                    }
                    break;
                }
                tokens.Add(new TextToken(text.Substring(start, i - start), TokenKind.Word, start, i - start, wordIndex));
                wordIndex++;
                continue;
            }

            // Everything else is punctuation (one char at a time).
            tokens.Add(new TextToken(text.Substring(i, 1), TokenKind.Punctuation, i, 1, null));
            i++;
        }

        return tokens;
    }

    /// <summary>Number of word tokens in the text.</summary>
    public static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (IsWordChar(c)) { if (!inWord) { count++; inWord = true; } }
            else if (c != '\'' && c != '’' && c != '-') inWord = false;
        }
        return count;
    }

    /// <summary>
    /// Normalizes a word for dictionary lookup / matching: lowercased, surrounding
    /// punctuation stripped, smart apostrophes folded to ASCII.
    /// </summary>
    public static string NormalizeWord(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
            else if (c == '’') sb.Append('\'');
            else if (c == '\'' || c == '-') sb.Append(c);
        }
        return sb.ToString().Trim('\'', '-');
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c);
}
