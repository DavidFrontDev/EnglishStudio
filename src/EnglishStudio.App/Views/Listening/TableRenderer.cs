using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EnglishStudio.App.ViewModels.Listening;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders a Table card's <see cref="ListeningCardViewModel.TableJson"/> into a bordered grid.
/// JSON shape: <c>{ "Title": "...", "Columns": ["Day","Activity","Notes"], "Rows": [[...],[...]] }</c>.
/// Any <c>{N}</c> inside a cell becomes an inline <see cref="GapInputBox"/>.
/// </summary>
public static class TableRenderer
{
    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(TableRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private sealed record TableModel(string? Title, List<string>? Columns, List<List<string>>? Rows);

    /// <summary>
    /// Splits a cell's text on sentence boundaries — a period (or "?"/"!") followed by a
    /// space and a capital letter or an opening parenthesis — so each sentence ends up on
    /// its own line inside the same cell. Numbers like "£9.75" and abbreviations like
    /// "5.30 a.m." don't match because the lookahead requires upper-case after the gap.
    /// </summary>
    private static readonly Regex SentenceSplit = new(@"(?<=[\.!?]) (?=[A-Z\(])", RegexOptions.Compiled);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Panel host) return;
        host.Children.Clear();
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrWhiteSpace(card.TableJson)) return;

        TableModel? model;
        try { model = JsonSerializer.Deserialize<TableModel>(card.TableJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { return; }
        if (model?.Rows is null || model.Rows.Count == 0) return;

        if (!string.IsNullOrWhiteSpace(model.Title))
        {
            var title = new TextBlock
            {
                Text = model.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Margin = new Thickness(0, 0, 0, 8)
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "StrongTextBrush");
            host.Children.Add(title);
        }

        var colCount = Math.Max(
            model.Columns?.Count ?? 0,
            model.Rows.Max(r => r.Count));
        if (colCount == 0) return;

        // Every column sizes to its widest cell (Auto), so the table hugs its content rather
        // than stretching to the card width.
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        for (var c = 0; c < colCount; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var rowOffset = 0;
        if (model.Columns is { Count: > 0 })
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < colCount; c++)
            {
                var header = model.Columns.ElementAtOrDefault(c) ?? string.Empty;
                grid.Children.Add(BuildCell(header, card, 0, c, isHeader: true));
            }
            rowOffset = 1;
        }

        for (var r = 0; r < model.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = model.Rows[r];
            for (var c = 0; c < colCount; c++)
            {
                var cellText = row.ElementAtOrDefault(c) ?? string.Empty;
                grid.Children.Add(BuildCell(cellText, card, r + rowOffset, c, isHeader: false));
            }
        }

        host.Children.Add(grid);
    }

    private static UIElement BuildCell(string text, ListeningCardViewModel card, int row, int col, bool isHeader)
    {
        // Split cell text on sentence boundaries — each sentence becomes its own TextBlock
        // stacked vertically inside the cell. NoWrap on each TextBlock means a single
        // sentence stays on one line, so the column's Auto width = the widest single
        // sentence across all cells in that column. Cells with only one sentence collapse
        // to a single line (no extra padding).
        var sentences = SentenceSplit.Split(text);

        var stack = new StackPanel();
        foreach (var sentence in sentences)
        {
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                LineHeight = 26,
                FontSize = 13,
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, isHeader ? "StrongTextBrush" : "PrimaryTextBrush");
            GapSegments.Populate(tb, sentence, card.GapByNumber);
            stack.Children.Add(tb);
        }

        var border = new Border
        {
            BorderThickness = new Thickness(0.6),
            Padding = new Thickness(10, 7, 10, 7),
            Child = stack
        };
        border.SetResourceReference(Border.BorderBrushProperty, "OverlayLight20Brush");
        if (isHeader) border.SetResourceReference(Border.BackgroundProperty, "OverlayLight13Brush");

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }
}
