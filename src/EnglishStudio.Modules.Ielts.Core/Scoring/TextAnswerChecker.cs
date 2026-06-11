using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Checks free-text answer types: T/F/NG, Y/N/NG, Short Answer, and all *Completion variants.
/// Enforces NMTW limit if present on the question.
/// </summary>
public sealed class TextAnswerChecker : IAnswerChecker
{
    private static readonly HashSet<QuestionType> Supported =
    [
        QuestionType.TrueFalseNotGiven,
        QuestionType.YesNoNotGiven,
        QuestionType.SentenceCompletion,
        QuestionType.SummaryCompletion,
        QuestionType.NoteCompletion,
        QuestionType.TableCompletion,
        QuestionType.FlowChartCompletion,
        QuestionType.ShortAnswer,
        QuestionType.FormCompletion
    ];

    public bool CanHandle(QuestionType type) => Supported.Contains(type);

    public AnswerCheckResult Check(TestQuestion question, string userAnswerJson)
    {
        var userAnswer = ExtractText(userAnswerJson);
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new AnswerCheckResult(false, 0);
        }

        // Build the set of accepted answers: AnswerKeyJson + optional AcceptableAnswersJson.
        var accepted = new List<string> { ExtractText(question.AnswerKeyJson) };
        if (!string.IsNullOrWhiteSpace(question.AcceptableAnswersJson))
        {
            try
            {
                var more = JsonSerializer.Deserialize<string[]>(question.AcceptableAnswersJson);
                if (more is not null) accepted.AddRange(more);
            }
            catch (JsonException)
            {
                // Treat as a single literal string if the JSON parse fails.
                accepted.Add(question.AcceptableAnswersJson);
            }
        }

        foreach (var candidate in accepted)
        {
            if (AnswerNormalization.Equivalent(userAnswer, candidate))
            {
                return new AnswerCheckResult(true, question.Points);
            }
        }

        // Enforce No-More-Than-N-Words limit only when no accepted answer matched.
        if (question.WordLimitMax is int limit && AnswerNormalization.CountWords(userAnswer) > limit)
        {
            return new AnswerCheckResult(false, 0, $"Word limit exceeded ({limit}).");
        }

        return new AnswerCheckResult(false, 0);
    }

    private static string ExtractText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var trimmed = raw.Trim();
        // The stored value may be plain text or a JSON string literal — try JSON first.
        if (trimmed.StartsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty; }
            catch (JsonException) { /* fall through */ }
        }
        return trimmed;
    }
}
