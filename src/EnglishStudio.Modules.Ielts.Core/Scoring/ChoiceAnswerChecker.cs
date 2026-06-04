using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Multiple-choice single (one correct option) and multi (N correct options, all must be selected).
/// AnswerKeyJson format:
///   • Single: "A"  or  JSON-encoded string "\"A\""
///   • Multi:  ["A", "C"]  (order ignored)
/// </summary>
public sealed class ChoiceAnswerChecker : IAnswerChecker
{
    public bool CanHandle(QuestionType type) =>
        type == QuestionType.MultipleChoiceSingle || type == QuestionType.MultipleChoiceMulti;

    public AnswerCheckResult Check(TestQuestion question, string userAnswerJson)
    {
        if (question.Type == QuestionType.MultipleChoiceSingle)
        {
            var user = ParseSingle(userAnswerJson);
            if (string.IsNullOrEmpty(user)) return new AnswerCheckResult(false, 0);

            // Accept the primary key plus any AcceptableAnswers (used for IELTS "choose TWO from A-E"
            // pairs where the two questions share an order-independent answer set).
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ParseSingle(question.AnswerKeyJson)
            };
            if (!string.IsNullOrWhiteSpace(question.AcceptableAnswersJson))
            {
                try
                {
                    var more = JsonSerializer.Deserialize<string[]>(question.AcceptableAnswersJson);
                    if (more is not null)
                    {
                        foreach (var s in more)
                            if (!string.IsNullOrWhiteSpace(s)) accepted.Add(s.Trim());
                    }
                }
                catch (JsonException) { /* ignore malformed */ }
            }
            return accepted.Contains(user)
                ? new AnswerCheckResult(true, question.Points)
                : new AnswerCheckResult(false, 0);
        }

        // Multi: every key option must be selected, no extras.
        var userSet = ParseArray(userAnswerJson);
        var keySet = ParseArray(question.AnswerKeyJson);
        if (userSet.Count == 0) return new AnswerCheckResult(false, 0);

        var match = userSet.SetEquals(keySet);
        return match
            ? new AnswerCheckResult(true, question.Points)
            : new AnswerCheckResult(false, 0);
    }

    private static string ParseSingle(string raw)
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

    private static HashSet<string> ParseArray(string raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(raw);
            if (arr is not null)
            {
                foreach (var s in arr)
                {
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
                }
            }
        }
        catch (JsonException)
        {
            // Tolerate plain-text fallback like "A,C".
            foreach (var s in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                result.Add(s.Trim());
            }
        }
        return result;
    }
}
