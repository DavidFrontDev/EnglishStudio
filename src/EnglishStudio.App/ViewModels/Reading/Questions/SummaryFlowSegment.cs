using System.Text.RegularExpressions;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>
/// One piece of a <see cref="QuestionGroupLayout.SummaryFlow"/> template: either a chunk of
/// plain text, or a numbered gap that references one of the group's child questions.
/// </summary>
public sealed record SummaryFlowSegment(SummaryFlowSegmentKind Kind, string? Text, int? GapNumber, IReadingQuestionViewModel? GapQuestion)
{
    public bool IsText => Kind == SummaryFlowSegmentKind.Text;
    public bool IsGap => Kind == SummaryFlowSegmentKind.Gap;
}

public enum SummaryFlowSegmentKind
{
    Text,
    Gap
}

internal static class SummaryFlowParser
{
    // Matches "{19}" or "{ 19 }" or "{__19__}" — number-only gap.
    private static readonly Regex GapRegex = new(@"\{[_\s]*(\d+)[_\s]*\}", RegexOptions.Compiled);

    /// <summary>Splits a template into Text / Gap segments. Gaps that don't map to a known question are emitted as plain text.</summary>
    public static IReadOnlyList<SummaryFlowSegment> Parse(string template, IReadOnlyDictionary<int, IReadingQuestionViewModel> questionsByDisplayNumber)
    {
        var result = new List<SummaryFlowSegment>();
        var cursor = 0;
        foreach (Match m in GapRegex.Matches(template))
        {
            if (m.Index > cursor)
            {
                var chunk = template.Substring(cursor, m.Index - cursor);
                if (chunk.Length > 0)
                {
                    result.Add(new SummaryFlowSegment(SummaryFlowSegmentKind.Text, chunk, null, null));
                }
            }

            var number = int.Parse(m.Groups[1].Value);
            if (questionsByDisplayNumber.TryGetValue(number, out var qVm))
            {
                result.Add(new SummaryFlowSegment(SummaryFlowSegmentKind.Gap, null, number, qVm));
            }
            else
            {
                // Unknown gap — emit literal text so author can spot the mismatch.
                result.Add(new SummaryFlowSegment(SummaryFlowSegmentKind.Text, m.Value, null, null));
            }

            cursor = m.Index + m.Length;
        }

        if (cursor < template.Length)
        {
            result.Add(new SummaryFlowSegment(SummaryFlowSegmentKind.Text, template.Substring(cursor), null, null));
        }

        return result;
    }
}
