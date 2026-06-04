using System.Text.RegularExpressions;
using System.Windows;

namespace EnglishStudio.App.ViewModels.Reading;

/// <summary>
/// One renderable chunk of a passage: either a labelled paragraph ("A. …") or a
/// subheading line between paragraphs.
/// </summary>
public sealed class PassageParagraphViewModel
{
    public string? Marker { get; }
    public string Body { get; }
    public FontStyle FontStyle { get; }
    public FontWeight FontWeight { get; }
    public bool HasMarker => !string.IsNullOrEmpty(Marker);

    private PassageParagraphViewModel(string? marker, string body, FontStyle style, FontWeight weight)
    {
        Marker = marker;
        Body = body;
        FontStyle = style;
        FontWeight = weight;
    }

    private static readonly Regex MarkerRegex = new(@"^([A-Z])\.\s+(.+)$", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// Splits a passage body into renderable items. Double newlines are paragraph breaks.
    /// Lines that match "^[A-Z]. " get the letter promoted to a Marker chip.
    /// Single-line blocks without a marker between two marked paragraphs are treated as subheadings.
    /// </summary>
    public static IReadOnlyList<PassageParagraphViewModel> Parse(string body)
    {
        var result = new List<PassageParagraphViewModel>();
        if (string.IsNullOrWhiteSpace(body)) return result;

        var blocks = Regex.Split(body, @"\r?\n\s*\r?\n");

        foreach (var raw in blocks)
        {
            var block = raw.Trim();
            if (block.Length == 0) continue;

            // Collapse single newlines inside a paragraph into spaces — seed uses one-sentence-per-line.
            var collapsed = Regex.Replace(block, @"\s*\r?\n\s*", " ").Trim();

            var match = MarkerRegex.Match(collapsed);
            if (match.Success)
            {
                result.Add(new PassageParagraphViewModel(
                    marker: match.Groups[1].Value,
                    body: match.Groups[2].Value.Trim(),
                    style: FontStyles.Normal,
                    weight: FontWeights.Normal));
            }
            else if (LooksLikeSubheading(collapsed))
            {
                result.Add(new PassageParagraphViewModel(
                    marker: null,
                    body: collapsed,
                    style: FontStyles.Italic,
                    weight: FontWeights.SemiBold));
            }
            else
            {
                result.Add(new PassageParagraphViewModel(
                    marker: null,
                    body: collapsed,
                    style: FontStyles.Normal,
                    weight: FontWeights.Normal));
            }
        }

        return result;
    }

    /// <summary>Heuristic: short single-line block without sentence-ending punctuation → subheading.</summary>
    private static bool LooksLikeSubheading(string text)
    {
        if (text.Length > 80) return false;
        if (text.Contains('.') || text.Contains('!') || text.Contains('?')) return false;
        return true;
    }
}
