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

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inToken = false;
            }
            else if (!inToken && IsWordChar(ch))
            {
                count++;
                inToken = true;
            }
            else if (inToken && !IsWordChar(ch) && !IsIntraWord(ch))
            {
                inToken = false;
            }
        }

        return count;
    }

    private static bool IsWordChar(char ch) => char.IsLetterOrDigit(ch);

    private static bool IsIntraWord(char ch) => ch is '-' or '\'' or '’' or '/' or '.';
}
