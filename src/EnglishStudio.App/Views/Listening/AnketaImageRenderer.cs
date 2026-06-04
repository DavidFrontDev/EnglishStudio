using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EnglishStudio.App.ViewModels.Listening;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Renders an AnketaImage card's <see cref="ListeningCardViewModel.AnketaImageMarkup"/> (flowing
/// summary/notes prose) into a wrapping <see cref="TextBlock"/> where every inline <c>{N}</c> gap
/// becomes a compact <see cref="GapComboBox"/> letter-picker bound to the matching question. The
/// list of selectable letters comes from the card's shared option box (legend image / text).
/// </summary>
public static class AnketaImageRenderer
{
    private static readonly Regex GapRegex = new(@"\{[_\s]*(\d+)[_\s]*\}", RegexOptions.Compiled);

    public static readonly DependencyProperty CardProperty = DependencyProperty.RegisterAttached(
        "Card",
        typeof(ListeningCardViewModel),
        typeof(AnketaImageRenderer),
        new PropertyMetadata(null, OnCardChanged));

    public static void SetCard(DependencyObject element, ListeningCardViewModel? value) => element.SetValue(CardProperty, value);
    public static ListeningCardViewModel? GetCard(DependencyObject element) => (ListeningCardViewModel?)element.GetValue(CardProperty);

    private static void OnCardChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        tb.TextWrapping = TextWrapping.Wrap;
        tb.LineHeight = 30;
        if (e.NewValue is not ListeningCardViewModel card || string.IsNullOrEmpty(card.AnketaImageMarkup)) return;

        var template = card.AnketaImageMarkup!;
        var cursor = 0;
        foreach (Match m in GapRegex.Matches(template))
        {
            if (m.Index > cursor)
                tb.Inlines.Add(new Run(template.Substring(cursor, m.Index - cursor)));

            var number = int.Parse(m.Groups[1].Value);
            if (card.MatchByNumber.TryGetValue(number, out var vm))
            {
                tb.Inlines.Add(new InlineUIContainer(new GapComboBox { Question = vm })
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
