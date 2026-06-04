using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Shared helper that turns a string containing "{N}" placeholders into TextBlock inlines:
/// plain runs interleaved with inline <see cref="GapInputBox"/> controls bound to the matching
/// gap question. Used by both the StructuredNotes (Anketa) and Table renderers.
/// </summary>
internal static class GapSegments
{
    private static readonly Regex GapRegex = new(@"\{\s*(\d+)\s*\}", RegexOptions.Compiled);

    public static void Populate(TextBlock target, string text, IReadOnlyDictionary<int, TextInputQuestionViewModel> gapByNumber)
    {
        var cursor = 0;
        foreach (Match m in GapRegex.Matches(text))
        {
            if (m.Index > cursor)
                target.Inlines.Add(new Run(text.Substring(cursor, m.Index - cursor)));

            var number = int.Parse(m.Groups[1].Value);
            if (gapByNumber.TryGetValue(number, out var vm))
            {
                var gap = new GapInputBox { Question = vm, Margin = new Thickness(2, 0, 2, 0) };
                target.Inlines.Add(new InlineUIContainer(gap) { BaselineAlignment = BaselineAlignment.Bottom });
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
