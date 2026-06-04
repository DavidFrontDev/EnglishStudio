using System.Text.Json;
using EnglishStudio.Modules.Ai.Evaluators;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Writing;

/// <summary>
/// Bridges a <see cref="WritingAttempt"/> with the AI essay evaluator,
/// persisting the band scores and feedback JSON back to the attempt.
/// </summary>
public sealed class WritingFeedbackService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IIeltsEssayEvaluator _evaluator;

    public WritingFeedbackService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        IIeltsEssayEvaluator evaluator)
    {
        _dbFactory = dbFactory;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Evaluates the attempt and saves the four-criterion bands + JSON report.
    /// Returns null if the CLI is unavailable or returned an unparseable response —
    /// in that case the attempt is left without bands and callers can show a fallback message.
    /// </summary>
    public async Task<EssayScoreReport?> EvaluateAndSaveAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.WritingAttempts
            .Include(a => a.WritingTask)
                .ThenInclude(t => t.ModelAnswers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException($"Writing attempt {attemptId} not found.");

        var taskType = ToAiTaskType(attempt.WritingTask.Kind);

        var references = attempt.WritingTask.ModelAnswers
            .OrderBy(m => m.BandLevel)
            .Select(m => new EssayReferenceExample(m.BandLevel, m.AnswerText, m.ExaminerComment))
            .ToList();

        var report = await _evaluator.EvaluateAsync(
            taskType,
            attempt.WritingTask.PromptText,
            attempt.UserText,
            references,
            attempt.WritingTask.ImagePath,
            ct);

        if (report is null) return null;

        attempt.BandTaskAchievement = report.TaskAchievement;
        attempt.BandCoherence = report.CoherenceCohesion;
        attempt.BandLexical = report.LexicalResource;
        attempt.BandGrammar = report.GrammaticalRangeAccuracy;
        attempt.BandOverall = report.Overall;
        attempt.FeedbackJson = JsonSerializer.Serialize(report);
        await db.SaveChangesAsync(ct);

        return report;
    }

    private static WritingTaskType ToAiTaskType(WritingTaskKind kind) => kind switch
    {
        WritingTaskKind.Task1Academic => WritingTaskType.Task1Academic,
        WritingTaskKind.Task1GeneralTraining => WritingTaskType.Task1GeneralTraining,
        WritingTaskKind.Task2 => WritingTaskType.Task2,
        _ => WritingTaskType.Task2
    };
}
