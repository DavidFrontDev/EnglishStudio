using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Writing;

public sealed class WritingTaskService : IWritingTaskService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;

    public WritingTaskService(IDbContextFactory<IeltsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<WritingTestSetSummary>> ListTestSetsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sets = await db.TestSets
            .AsNoTracking()
            .Where(t => t.Section == IeltsSection.Writing)
            .OrderBy(t => t.Title)
            .ThenBy(t => t.Code)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Title,
                t.AuthorAttribution,
                Tasks = db.WritingTasks
                    .Where(w => w.TestSetId == t.Id)
                    .OrderBy(w => w.OrderInSet)
                    .Select(w => new
                    {
                        w.Id,
                        w.OrderInSet,
                        w.PromptText,
                        w.ChartType,
                        w.TopicCategory
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        var results = new List<WritingTestSetSummary>();
        foreach (var s in sets)
        {
            var t1 = s.Tasks.FirstOrDefault(t => t.OrderInSet == 1);
            var t2 = s.Tasks.FirstOrDefault(t => t.OrderInSet == 2);
            if (t1 is null || t2 is null) continue;

            var taskIds = new[] { t1.Id, t2.Id };
            var completed = await db.WritingAttempts
                .CountAsync(a => taskIds.Contains(a.WritingTaskId) && a.SubmittedAt != null, ct);

            var lastBand = await db.WritingAttempts
                .Where(a => taskIds.Contains(a.WritingTaskId) && a.BandOverall != null)
                .OrderByDescending(a => a.SubmittedAt)
                .Select(a => a.BandOverall)
                .FirstOrDefaultAsync(ct);

            results.Add(new WritingTestSetSummary(
                s.Id, s.Code, s.Title, s.AuthorAttribution,
                t1.Id, Preview(t1.PromptText), t1.ChartType,
                t2.Id, Preview(t2.PromptText), t2.TopicCategory,
                completed, lastBand));
        }
        return results;
    }

    public async Task<WritingTestSetDetail?> GetTestSetAsync(int testSetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var testSet = await db.TestSets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == testSetId && t.Section == IeltsSection.Writing, ct);
        if (testSet is null) return null;

        var tasks = await db.WritingTasks
            .AsNoTracking()
            .Include(t => t.ModelAnswers.OrderBy(m => m.BandLevel))
            .Where(t => t.TestSetId == testSetId)
            .OrderBy(t => t.OrderInSet)
            .ToListAsync(ct);

        var task1 = tasks.FirstOrDefault(t => t.OrderInSet == 1);
        var task2 = tasks.FirstOrDefault(t => t.OrderInSet == 2);
        if (task1 is null || task2 is null) return null;

        return new WritingTestSetDetail(testSet, task1, task2);
    }

    public async Task<IReadOnlyList<WritingTaskSummary>> ListAsync(WritingTaskKind kind, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Only standalone drills (not part of a full TestSet) appear in the per-kind list.
        var rows = await db.WritingTasks
            .AsNoTracking()
            .Where(t => t.Kind == kind && t.TestSetId == null)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Kind,
                t.PromptText,
                t.TopicCategory,
                t.ChartType,
                t.MinWords,
                t.RecommendedMinutes,
                AttemptCount = db.WritingAttempts.Count(a => a.WritingTaskId == t.Id),
                LastBand = db.WritingAttempts
                    .Where(a => a.WritingTaskId == t.Id && a.SubmittedAt != null && a.BandOverall != null)
                    .OrderByDescending(a => a.SubmittedAt)
                    .Select(a => a.BandOverall)
                    .FirstOrDefault()
            })
            .OrderBy(t => t.Code)
            .ToListAsync(ct);

        return rows.ConvertAll(r => new WritingTaskSummary(
            r.Id, r.Code, r.Kind,
            Preview(r.PromptText),
            r.TopicCategory, r.ChartType,
            r.MinWords, r.RecommendedMinutes,
            r.AttemptCount, r.LastBand));
    }

    public async Task<WritingTask?> GetFullAsync(int taskId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.WritingTasks
            .AsNoTracking()
            .Include(t => t.ModelAnswers.OrderBy(m => m.BandLevel))
            .FirstOrDefaultAsync(t => t.Id == taskId, ct);
    }

    public async Task<WritingAttempt> StartAttemptAsync(int taskId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = new WritingAttempt
        {
            WritingTaskId = taskId,
            StartedAt = DateTime.UtcNow,
            UserText = string.Empty,
            WordCount = 0
        };
        db.WritingAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task SaveDraftAsync(int attemptId, string userText, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.WritingAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException($"Writing attempt {attemptId} not found.");

        attempt.UserText = userText ?? string.Empty;
        attempt.WordCount = IeltsWordCounter.Count(attempt.UserText);
        await db.SaveChangesAsync(ct);
    }

    public async Task<WritingAttempt> SubmitAttemptAsync(int attemptId, string userText, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.WritingAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct)
            ?? throw new InvalidOperationException($"Writing attempt {attemptId} not found.");

        var now = DateTime.UtcNow;
        attempt.UserText = userText ?? string.Empty;
        attempt.WordCount = IeltsWordCounter.Count(attempt.UserText);
        attempt.SubmittedAt = now;
        attempt.DurationSeconds = (int)Math.Max(0, (now - attempt.StartedAt).TotalSeconds);
        await db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task<IReadOnlyList<WritingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WritingAttempts
            .AsNoTracking()
            .OrderByDescending(a => a.StartedAt)
            .Take(limit)
            .Select(a => new WritingAttemptSummary(
                a.Id,
                a.WritingTaskId,
                a.WritingTask.Code,
                a.WritingTask.Kind,
                a.StartedAt,
                a.SubmittedAt,
                a.WordCount,
                a.BandOverall))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<WritingHistoryEntry>> ListHistoryAsync(int limit = 500, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.WritingAttempts
            .AsNoTracking()
            .OrderByDescending(a => a.StartedAt)
            .Take(limit)
            .Select(a => new WritingHistoryEntry(
                a.Id,
                a.WritingTaskId,
                a.WritingTask.Code,
                a.WritingTask.TestSet != null ? a.WritingTask.TestSet.Title : a.WritingTask.Code,
                a.WritingTask.Kind,
                a.StartedAt,
                a.SubmittedAt,
                a.WordCount,
                a.DurationSeconds,
                a.BandTaskAchievement,
                a.BandCoherence,
                a.BandLexical,
                a.BandGrammar,
                a.BandOverall))
            .ToListAsync(ct);
    }

    public async Task<WritingAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.WritingAttempts
            .AsNoTracking()
            .Include(a => a.WritingTask)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
    }

    public async Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.WritingAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return;
        db.WritingAttempts.Remove(attempt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ClearHistoryAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.WritingAttempts.ExecuteDeleteAsync(ct);
    }

    private static string Preview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var single = text.Replace('\n', ' ').Replace('\r', ' ');
        single = System.Text.RegularExpressions.Regex.Replace(single, @"\s+", " ").Trim();
        return single.Length <= 220 ? single : single[..220].TrimEnd() + "…";
    }
}
