using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EnglishStudio.App.Views.Reading;

/// <summary>
/// Renders a reading passage into a wrapping <see cref="TextBlock"/>, giving each lettered section
/// marker ("A.", "B.", …) at the start of a paragraph a bold, distinctly-coloured run so the reader
/// can match the "which section A-G" questions to the passage at a glance. Passages that don't use a
/// lettered A/B/… scheme render as plain text (no marker styling).
/// </summary>
public static class PassageRenderer
{
    // A paragraph-leading section label: a single capital letter + "." or ")" + whitespace.
    private static readonly Regex Marker = new(@"^([A-Z])[\.\)]\s+", RegexOptions.Compiled);

    // Distinct hues that stay legible on both the dark and light palettes (medium luminance).
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0xE0, 0x6C, 0x75), // red
        Color.FromRgb(0xD1, 0x9A, 0x66), // amber
        Color.FromRgb(0x3F, 0xA6, 0x6A), // green
        Color.FromRgb(0x3F, 0x9F, 0xE0), // blue
        Color.FromRgb(0xA8, 0x6F, 0xD6), // purple
        Color.FromRgb(0x2F, 0xB4, 0xB4), // teal
        Color.FromRgb(0xD2, 0x68, 0x5A), // terracotta
        Color.FromRgb(0x7E, 0x8B, 0xD9), // indigo
    };

    public static readonly DependencyProperty BodyProperty = DependencyProperty.RegisterAttached(
        "Body",
        typeof(string),
        typeof(PassageRenderer),
        new PropertyMetadata(null, OnBodyChanged));

    public static void SetBody(DependencyObject element, string? value) => element.SetValue(BodyProperty, value);
    public static string? GetBody(DependencyObject element) => (string?)element.GetValue(BodyProperty);

    private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();

        var body = e.NewValue as string;
        if (string.IsNullOrEmpty(body)) return;

        var lines = body.Replace("\r\n", "\n").Split('\n');

        // Only treat leading letters as section markers when the passage actually uses a lettered
        // A/B/… scheme; otherwise a paragraph that merely starts with an initial ("J. Smith said…")
        // shouldn't be mistaken for a section label.
        var lettered = HasLetteredSequence(lines);

        var first = true;
        foreach (var line in lines)
        {
            if (!first) tb.Inlines.Add(new LineBreak());
            first = false;

            var m = lettered ? Marker.Match(line) : Match.Empty;
            if (m.Success)
            {
                var letter = m.Groups[1].Value[0];
                var color = Palette[(letter - 'A') % Palette.Length];
                tb.Inlines.Add(new Run(line.Substring(0, m.Length).TrimEnd())
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(color)
                });
                tb.Inlines.Add(new Run(" " + line.Substring(m.Length)));
            }
            else
            {
                tb.Inlines.Add(new Run(line));
            }
        }
    }

    // A passage is "lettered" when at least the first two markers (A and B) are present at
    // paragraph starts — enough to confirm an intentional A/B/… section scheme.
    private static bool HasLetteredSequence(string[] lines)
    {
        bool hasA = false, hasB = false;
        foreach (var line in lines)
        {
            var m = Marker.Match(line);
            if (!m.Success) continue;
            if (m.Groups[1].Value == "A") hasA = true;
            else if (m.Groups[1].Value == "B") hasB = true;
            if (hasA && hasB) return true;
        }
        return false;
    }
}
