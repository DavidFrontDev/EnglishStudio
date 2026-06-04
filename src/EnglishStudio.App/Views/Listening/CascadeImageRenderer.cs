using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders a CascadeImage (flow chart with letter-box) card. Identical layout to
/// <see cref="CascadeRenderer"/> — title + bordered blocks separated by ↓ — but inline
/// <c>{N}</c> gaps are replaced by an inline <see cref="GapComboBox"/> bound to the matching
/// <see cref="MatchingQuestionViewModel"/> (letter picker over the shared A–H box).
/// </summary>
public static class CascadeImageRenderer
{
    private static readonly Regex GapRegex = new(@"\{\s*(\d+)\s*\}", RegexOptions.Compiled);

    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(CascadeImageRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel host) return;
        host.Children.Clear();
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrEmpty(card.CascadeMarkup)) return;

        var lines = card.CascadeMarkup.Replace("\r\n", "\n").Split('\n');

        var blocks = new List<List<string>>();
        var current = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                host.Children.Add(BuildTitle(line[2..]));
                continue;
            }
            if (line.Trim() == "===")
            {
                if (current.Count > 0) { blocks.Add(current); current = new List<string>(); }
                continue;
            }
            if (line.Length == 0 && current.Count == 0) continue;
            current.Add(line);
        }
        if (current.Count > 0) blocks.Add(current);

        for (var i = 0; i < blocks.Count; i++)
        {
            host.Children.Add(BuildBlock(blocks[i], card, host));
            if (i < blocks.Count - 1) host.Children.Add(BuildArrow());
        }
    }

    private static UIElement BuildTitle(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "StrongTextBrush");
        return tb;
    }

    private static UIElement BuildArrow()
    {
        var arrow = new TextBlock
        {
            Text = "↓",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 3)
        };
        arrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentHotBrush");
        return arrow;
    }

    private static UIElement BuildBlock(List<string> blockLines, ListeningCardViewModel card, Panel host)
    {
        var stack = new StackPanel();
        foreach (var line in blockLines)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                LineHeight = 30,
                FontSize = 14,
                Margin = new Thickness(0, 2, 0, 2)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            PopulateWithCombos(tb, line, card.MatchByNumber);
            stack.Children.Add(tb);
        }

        var border = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 10, 16, 10),
            Child = stack
        };
        border.SetResourceReference(Border.BackgroundProperty, "OverlayDark20Brush");
        border.SetResourceReference(Border.BorderBrushProperty, "OverlayLight20Brush");

        // Wrap each block in a SharedSizeGroup grid so all cascade blocks end up the same width
        // = the widest block's natural width. The host has Grid.IsSharedSizeScope=True from XAML.
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "CascadeImgBlk"
        });
        Grid.SetColumn(border, 0);
        grid.Children.Add(border);
        return grid;
    }

    /// <summary>
    /// Same logic as <see cref="GapSegments.Populate"/> but emits <see cref="GapComboBox"/> for
    /// each "{N}" instead of <see cref="GapInputBox"/>.
    /// </summary>
    private static void PopulateWithCombos(TextBlock target, string text, IReadOnlyDictionary<int, MatchingQuestionViewModel> matchByNumber)
    {
        var cursor = 0;
        foreach (Match m in GapRegex.Matches(text))
        {
            if (m.Index > cursor)
                target.Inlines.Add(new Run(text.Substring(cursor, m.Index - cursor)));

            var number = int.Parse(m.Groups[1].Value);
            if (matchByNumber.TryGetValue(number, out var vm))
            {
                var combo = new GapComboBox { Question = vm, Margin = new Thickness(2, 0, 2, 0) };
                target.Inlines.Add(new InlineUIContainer(combo) { BaselineAlignment = BaselineAlignment.Bottom });
            }
            else
            {
                target.Inlines.Add(new Run(m.Value));
            }
            cursor = m.Index + m.Length;
        }

        if (cursor < text.Length)
            target.Inlines.Add(new Run(text.Substring(cursor)));
    }
}
