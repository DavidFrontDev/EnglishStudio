using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Listening;

public sealed class ListeningTestService : IListeningTestService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;

    public ListeningTestService(IDbContextFactory<IeltsDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IReadOnlyList<ListeningTestSummary>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var raw = await db.TestSets
            .AsNoTracking()
            .Where(t => t.Section == IeltsSection.Listening)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Title,
                t.Mode,
                t.IsExamOnly,
                Parts = t.Parts.Count,
                Questions = t.Parts.SelectMany(p => p.Questions).Count(),
                LastBand = db.TestAttempts
                    .Where(a => a.TestSetId == t.Id && a.FinishedAt != null)
                    .OrderByDescending(a => a.FinishedAt)
                    .Select(a => (double?)a.BandEstimate)
                    .FirstOrDefault()
            })
            .OrderBy(t => t.Code)
            .ToListAsync(ct);

        return raw.ConvertAll(r => new ListeningTestSummary(
            r.Id, r.Code, r.Title, r.Mode, r.Parts, r.Questions, r.LastBand, r.IsExamOnly));
    }

    public async Task<TestSet?> GetFullAsync(int testSetId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TestSets
            .AsNoTracking()
            .Include(t => t.Parts.OrderBy(p => p.OrderInTest))
                .ThenInclude(p => p.Questions.OrderBy(q => q.OrderInPart))
            .Include(t => t.Parts)
                .ThenInclude(p => p.Groups.OrderBy(g => g.OrderInPart))
                    .ThenInclude(g => g.Questions.OrderBy(q => q.OrderInPart))
            .FirstOrDefaultAsync(t => t.Id == testSetId && t.Section == IeltsSection.Listening, ct);
    }

    public async Task<IReadOnlyList<ListeningAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.TestAttempts
            .AsNoTracking()
            .Where(a => a.TestSet.Section == IeltsSection.Listening && a.FinishedAt != null)
            .OrderByDescending(a => a.StartedAt)
            .Take(limit)
            .Select(a => new ListeningAttemptSummary(
                a.Id,
                a.TestSetId,
                a.TestSet.Title,
                a.StartedAt,
                a.FinishedAt,
                a.RawScore,
                a.BandEstimate,
                a.IsTrainingMode))
            .ToListAsync(ct);
    }

    public async Task<int> CountAttemptsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TestAttempts
            .AsNoTracking()
            .Where(a => a.TestSet.Section == IeltsSection.Listening)
            .CountAsync(ct);
    }

    public async Task<TestAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TestAttempts
            .AsNoTracking()
            .Include(a => a.TestSet)
                .ThenInclude(t => t.Parts.OrderBy(p => p.OrderInTest))
                    .ThenInclude(p => p.Questions.OrderBy(q => q.OrderInPart))
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
    }

    public async Task<int> ClearAllAttemptsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var attemptIds = await db.TestAttempts
            .Where(a => a.TestSet.Section == IeltsSection.Listening)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (attemptIds.Count == 0) return 0;

        await db.TestAttempts
            .Where(a => attemptIds.Contains(a.Id))
            .ExecuteDeleteAsync(ct);

        return attemptIds.Count;
    }
}
