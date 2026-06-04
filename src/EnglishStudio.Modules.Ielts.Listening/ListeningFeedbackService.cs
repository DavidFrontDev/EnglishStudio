using System.Text.Json;
using EnglishStudio.Modules.Ai.Evaluators;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Listening;

/// <summary>
/// Bridges a completed Listening <see cref="TestAttempt"/> with the AI evaluator,
/// persisting the resulting <see cref="ListeningScoreReport"/> JSON back to
/// <see cref="TestAttempt.FeedbackJson"/>. Mirrors <c>WritingFeedbackService</c>.
/// </summary>
public sealed class ListeningFeedbackService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IIeltsListeningEvaluator _evaluator;

    public ListeningFeedbackService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        IIeltsListeningEvaluator evaluator)
    {
        _dbFactory = dbFactory;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Loads the saved AI report for an attempt, if any. Returns null if the user has not
    /// requested one yet (FeedbackJson empty) or if the stored JSON is malformed.
    /// </summary>
    public async Task<ListeningScoreReport?> LoadSavedAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.TestAttempts
            .Where(a => a.Id == attemptId)
            .Select(a => new { a.FeedbackJson })
            .FirstOrDefaultAsync(ct);
        if (attempt is null || string.IsNullOrWhiteSpace(attempt.FeedbackJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<ListeningScoreReport>(
                attempt.FeedbackJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Calls the AI evaluator for the attempt and saves the report.
    /// Returns null if CLI is unavailable / response unparseable — caller can fall back to a message.
    /// </summary>
    public async Task<ListeningScoreReport?> EvaluateAndSaveAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.TestAttempts
            .Include(a => a.TestSet).ThenInclude(t => t.Parts).ThenInclude(p => p.Questions)
            .Include(a => a.Answers).ThenInclude(an => an.TestQuestion)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException($"Test attempt {attemptId} not found.");

        var totalQuestions = attempt.TestSet.Parts.Sum(p => p.Questions.Count);
        var byQid = attempt.Answers.ToDictionary(a => a.TestQuestionId, a => a);

        var parts = attempt.TestSet.Parts
            .OrderBy(p => p.OrderInTest)
            .Select(p => new ListeningPartContext(
                PartNumber: p.OrderInTest,
                PartTitle: p.Title,
                Transcript: p.Transcript ?? string.Empty,
                Questions: p.Questions
                    .OrderBy(q => q.OrderInPart)
                    .Select(q =>
                    {
                        byQid.TryGetValue(q.Id, out var ans);
                        return new ListeningQuestionContext(
                            QuestionNumber: q.OrderInPart,
                            QuestionType: q.Type.ToString(),
                            Stem: q.Stem,
                            UserAnswer: ans is not null ? Strip(ans.UserAnswerJson) : string.Empty,
                            CorrectAnswer: Strip(q.AnswerKeyJson),
                            IsCorrect: ans?.IsCorrect ?? false);
                    })
                    .ToList()))
            .ToList();

        var report = await _evaluator.EvaluateAsync(
            attempt.TestSet.Title,
            attempt.RawScore,
            totalQuestions,
            attempt.BandEstimate,
            parts,
            ct);

        if (report is null) return null;

        attempt.FeedbackJson = JsonSerializer.Serialize(report);
        await db.SaveChangesAsync(ct);
        return report;
    }

    /// <summary>JSON-string answers are stored as quoted scalars; strip the quotes for display/AI.</summary>
    private static string Strip(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        var trimmed = json.Trim();
        if (trimmed.StartsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(trimmed) ?? trimmed; }
            catch (JsonException) { return trimmed.Trim('"'); }
        }
        return trimmed;
    }
}
