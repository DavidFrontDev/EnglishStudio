using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EnglishStudio.App.ViewModels.Listening;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders a SummaryFlow card's <see cref="ListeningCardViewModel.NotesTemplate"/> (flowing
/// summary prose) into a WRAPPING <see cref="TextBlock"/> where every inline <c>{N}</c> gap
/// becomes an inline <see cref="GapInputBox"/> free-text field. Unlike the notes renderer this
/// wraps, because a summary is one long paragraph rather than short bullet lines.
/// </summary>
public static class SummaryFlowGapRenderer
{
    private static readonly Regex GapRegex = new(@"\{[_\s]*(\d+)[_\s]*\}", RegexOptions.Compiled);

    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(SummaryFlowGapRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        tb.TextWrapping = TextWrapping.Wrap;
        tb.LineHeight = 32;
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrEmpty(card.NotesTemplate)) return;

        var template = card.NotesTemplate!;
        var cursor = 0;
        foreach (Match m in GapRegex.Matches(template))
        {
            if (m.Index > cursor)
                tb.Inlines.Add(new Run(template.Substring(cursor, m.Index - cursor)));

            var number = int.Parse(m.Groups[1].Value);
            if (card.GapByNumber.TryGetValue(number, out var vm))
            {
                tb.Inlines.Add(new InlineUIContainer(new GapInputBox { Question = vm })
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
            }
            else
            {
                tb.Inlines.Add(new Run(m.Value));
            }
            cursor = m.Index + m.Length;
        }

        if (cursor < template.Length)
            tb.Inlines.Add(new Run(template.Substring(cursor)));
    }
}
