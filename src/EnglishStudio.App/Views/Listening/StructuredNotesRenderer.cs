using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EnglishStudio.App.ViewModels.Listening;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders an Anketa card's <see cref="ListeningCardViewModel.NotesTemplate"/> into a vertical
/// stack of lines with inline gap fields. Line markup:
/// <list type="bullet">
/// <item><c># </c> — card title</item>
/// <item><c>## </c> — section heading</item>
/// <item><c>### </c> — sub-section heading</item>
/// <item><c>- </c> — bullet, <c>-- </c> — sub-bullet</item>
/// <item>anything else — plain line</item>
/// </list>
/// Any <c>{N}</c> inside a line becomes a <see cref="GapInputBox"/>.
/// </summary>
public static class StructuredNotesRenderer
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(StructuredNotesRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel host) return;
        host.Children.Clear();
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrEmpty(card.NotesTemplate)) return;

        var lines = card.NotesTemplate.Replace("\r\n", "\n").Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                host.Children.Add(new Border { Height = 6 });
                continue;
            }
            host.Children.Add(BuildLine(line, card));
        }
    }

    private static UIElement BuildLine(string line, ListeningCardViewModel card)
    {
        // Determine line kind by prefix.
        string content;
        double left = 0, top = 2, bottom = 2, fontSize = 14;
        var bold = false;
        string? bullet = null;
        var strong = false;

        if (line.StartsWith("### ", StringComparison.Ordinal))
        {
            content = line[4..]; bold = true; fontSize = 13; top = 8; bottom = 2; strong = true;
        }
        else if (line.StartsWith("## ", StringComparison.Ordinal))
        {
            content = line[3..]; bold = true; fontSize = 14.5; top = 12; bottom = 4; strong = true;
        }
        else if (line.StartsWith("# ", StringComparison.Ordinal))
        {
            content = line[2..]; bold = true; fontSize = 17; top = 0; bottom = 8; strong = true;
        }
        else if (line.StartsWith("-- ", StringComparison.Ordinal))
        {
            content = line[3..]; left = 38; bullet = "◦  ";
        }
        else if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            content = line[2..]; left = 18; bullet = "•  ";
        }
        else
        {
            content = line;
        }

        var tb = new TextBlock
        {
            // NoWrap so the outer StackPanel measures the card's natural width as the
            // longest line. The Anketa template wraps this in a HorizontalAlignment="Left"
            // Border, so the result is "card width = longest line + padding".
            TextWrapping = TextWrapping.NoWrap,
            LineHeight = 30,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(left, top, 0, bottom)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, strong ? "StrongTextBrush" : "PrimaryTextBrush");

        if (bullet is not null)
            tb.Inlines.Add(new Run(bullet) { FontWeight = FontWeights.Bold });

        GapSegments.Populate(tb, content, card.GapByNumber);
        return tb;
    }
}
