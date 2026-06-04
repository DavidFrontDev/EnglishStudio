using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Matching-type questions: each item maps to a tag/letter (Matching Headings, Matching Information,
/// Matching Features, Matching Sentence Endings, Map/Diagram Labeling).
///
/// AnswerKeyJson format:   {"1": "A", "2": "C", "3": "B"}
/// UserAnswerJson format:  same shape — keys are item codes, values are selected tags.
///
/// Scoring: one point per question; the entity stores ONE question per item in IELTS (each
/// matching "row" is its own TestQuestion), so this checker compares a single key-value.
/// </summary>
public sealed class MatchingAnswerChecker : IAnswerChecker
{
    public bool CanHandle(QuestionType type) =>
        type == QuestionType.MatchingHeadings ||
        type == QuestionType.MatchingInformation ||
        type == QuestionType.MatchingFeatures ||
        type == QuestionType.MatchingSentenceEndings ||
        type == QuestionType.MapLabeling ||
        type == QuestionType.DiagramLabeling;

    public AnswerCheckResult Check(TestQuestion question, string userAnswerJson)
    {
        var user = ExtractTag(userAnswerJson);
        var key = ExtractTag(question.AnswerKeyJson);
        if (string.IsNullOrEmpty(user)) return new AnswerCheckResult(false, 0);

        return string.Equals(user, key, StringComparison.OrdinalIgnoreCase)
            ? new AnswerCheckResult(true, question.Points)
            : new AnswerCheckResult(false, 0);
    }

    private static string ExtractTag(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('"'))
        {
            try { return (JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty).Trim(); }
            catch (JsonException) { /* fall through */ }
        }
        return trimmed;
    }
}
