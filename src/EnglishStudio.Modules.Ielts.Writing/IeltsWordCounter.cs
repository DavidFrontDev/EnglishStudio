namespace EnglishStudio.Modules.Ielts.Writing;

/// <summary>
/// Counts words using the IELTS Writing convention: tokens separated by whitespace,
/// hyphenated words and contractions count as one. Pure punctuation tokens are ignored.
/// </summary>
public static class IeltsWordCounter
{
    public static int Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var count = 0;
        var inToken = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch))
            {
                inToken = false;
            }
            else if (!inToken && IsWordChar(ch))
            {
                count++;
                inToken = true;
            }
            else if (inToken && !IsWordChar(ch) && !IsIntraWord(text, i))
            {
                inToken = false;
            }
        }

        return count;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch);

    private static bool IsIntraWord(string text, int i)
    {
        var ch = text[i];
        if (ch is '-' or '\'' or '’' or '/' or '.') return true;
        return ch == ','
            && i > 0 && char.IsDigit(text[i - 1])
            && i + 1 < text.Length && char.IsDigit(text[i + 1]);
    }
}
