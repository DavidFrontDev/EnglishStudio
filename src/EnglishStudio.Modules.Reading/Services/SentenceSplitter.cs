using System.Text;
using System.Text.RegularExpressions;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Splits tokenized text into sentences for shadowing, on terminal punctuation (. ! ?).
/// Pure (no dependencies). Each <see cref="ShadowingSentence"/> carries the word-index range so the
/// UI can highlight it and the analysis can slice the matching reference tokens.
/// </summary>
public sealed partial class SentenceSplitter
{
    public IReadOnlyList<ShadowingSentence> Split(IReadOnlyList<TextToken> tokens)
    {
        var sentences = new List<ShadowingSentence>();
        if (tokens is null || tokens.Count == 0) return sentences;

        var sb = new StringBuilder();
        var firstWord = -1;
        var lastWord = -1;
        var index = 0;

        void Flush()
        {
            if (firstWord >= 0 && lastWord >= 0)
            {
                var text = Clean(sb.ToString());
                if (text.Length > 0)
                    sentences.Add(new ShadowingSentence(index++, text, firstWord, lastWord));
            }
            sb.Clear();
            firstWord = -1;
            lastWord = -1;
        }

        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case TokenKind.Word:
                    if (firstWord < 0) firstWord = t.WordIndex ?? firstWord;
                    if (t.WordIndex.HasValue) lastWord = t.WordIndex.Value;
                    sb.Append(t.Text);
                    break;
                case TokenKind.Space:
                case TokenKind.Break:
                    sb.Append(' ');
                    break;
                case TokenKind.Punctuation:
                    sb.Append(t.Text);
                    if (IsTerminal(t.Text) && firstWord >= 0)
                        Flush();
                    break;
            }
        }

        Flush(); // trailing text without terminal punctuation
        return sentences;
    }

    private static bool IsTerminal(string punct) =>
        punct.Length == 1 && (punct[0] == '.' || punct[0] == '!' || punct[0] == '?');

    /// <summary>Collapse whitespace and drop spaces before punctuation for a clean display string.</summary>
    private static string Clean(string raw)
    {
        var collapsed = WhitespaceRegex().Replace(raw, " ").Trim();
        return SpaceBeforePunctRegex().Replace(collapsed, "$1");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s+([.,!?;:])")]
    private static partial Regex SpaceBeforePunctRegex();
}
