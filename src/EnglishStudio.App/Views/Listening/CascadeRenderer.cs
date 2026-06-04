using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Listening;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders a Cascade (flow chart) card from <see cref="ListeningCardViewModel.CascadeMarkup"/>:
/// an optional centred title (a leading <c>#</c> line), then a vertical chain of bordered blocks
/// separated by lines of <c>===</c>, with a downward arrow between consecutive blocks. Each line
/// inside a block is centred and may contain inline <c>{N}</c> gaps.
/// </summary>
public static class CascadeRenderer
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(CascadeRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel host) return;
        host.Children.Clear();
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrEmpty(card.CascadeMarkup)) return;

        var lines = card.CascadeMarkup.Replace("\r\n", "\n").Split('\n');

        // Group lines into blocks separated by "===". A leading "# Title" line becomes a centred title.
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
                LineHeight = 28,
                FontSize = 14,
                Margin = new Thickness(0, 2, 0, 2)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            GapSegments.Populate(tb, line, card.GapByNumber);
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

        // Wrap the border in a single-column Grid whose column joins SharedSizeGroup="CascadeBlk".
        // The host StackPanel has Grid.IsSharedSizeScope=True (set in CardTemplates.xaml), so all
        // blocks in the cascade end up with the same width = the widest block's natural width.
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "CascadeBlk"
        });
        Grid.SetColumn(border, 0);
        grid.Children.Add(border);
        return grid;
    }
}
