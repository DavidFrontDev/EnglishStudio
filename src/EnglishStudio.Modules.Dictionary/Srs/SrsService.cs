using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Srs;

public sealed class SrsService : ISrsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFsrsScheduler _scheduler;
    private readonly ILogger<SrsService> _logger;

    public SrsService(
        IServiceScopeFactory scopeFactory,
        IFsrsScheduler scheduler,
        ILogger<SrsService> logger)
    {
        _scopeFactory = scopeFactory;
        _scheduler = scheduler;
        _logger = logger;
    }

    public async Task<UserWordProgress?> AddWordAsync(int wordId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        var existing = await db.UserWordProgress.FirstOrDefaultAsync(p => p.WordId == wordId, ct);
        if (existing is not null) return existing;
        var now = DateTime.UtcNow;
        var p = new UserWordProgress
        {
            WordId = wordId,
            State = SrsState.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.UserWordProgress.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<UserWordProgress?> AddPhrasalVerbAsync(int phrasalVerbId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        var existing = await db.UserWordProgress.FirstOrDefaultAsync(p => p.PhrasalVerbId == phrasalVerbId, ct);
        if (existing is not null) return existing;
        var now = DateTime.UtcNow;
        var p = new UserWordProgress
        {
            PhrasalVerbId = phrasalVerbId,
            State = SrsState.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.UserWordProgress.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<UserWordProgress?> AddCollocationAsync(int collocationId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        var existing = await db.UserWordProgress.FirstOrDefaultAsync(p => p.CollocationId == collocationId, ct);
        if (existing is not null) return existing;
        var now = DateTime.UtcNow;
        var p = new UserWordProgress
        {
            CollocationId = collocationId,
            State = SrsState.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.UserWordProgress.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<bool> IsInTrainingForWordAsync(int wordId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        return await db.UserWordProgress.AnyAsync(p => p.WordId == wordId, ct);
    }

    public async Task<List<UserWordProgress>> BuildSessionAsync(int maxNew, int maxReview, DateTime now, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var dueQuery = db.UserWordProgress
            .Where(p => p.State != SrsState.New && p.NextReviewAt != null && p.NextReviewAt <= now)
            .OrderBy(p => p.NextReviewAt);
        var due = await IncludeOwners(dueQuery).Take(maxReview).ToListAsync(ct);

        var newCardsQuery = db.UserWordProgress
            .Where(p => p.State == SrsState.New)
            .OrderBy(p => p.CreatedAt);
        var newCards = await IncludeOwners(newCardsQuery).Take(maxNew).ToListAsync(ct);

        // Interleave: prefer reviews first if any, but mix new cards in.
        var result = new List<UserWordProgress>(due.Count + newCards.Count);
        var dueQ = new Queue<UserWordProgress>(due);
        var newQ = new Queue<UserWordProgress>(newCards);
        var newRatio = (newCards.Count == 0 || due.Count == 0)
            ? -1
            : Math.Max(1, due.Count / Math.Max(1, newCards.Count));
        var pickedSinceNew = 0;
        while (dueQ.Count > 0 || newQ.Count > 0)
        {
            if (newRatio > 0 && newQ.Count > 0 && (pickedSinceNew >= newRatio || dueQ.Count == 0))
            {
                result.Add(newQ.Dequeue());
                pickedSinceNew = 0;
            }
            else if (dueQ.Count > 0)
            {
                result.Add(dueQ.Dequeue());
                pickedSinceNew++;
            }
            else
            {
                result.Add(newQ.Dequeue());
            }
        }
        return result;
    }

    public async Task<List<UserWordProgress>> BuildSessionForWordIdsAsync(IReadOnlyCollection<int> wordIds, DateTime now, CancellationToken ct = default)
    {
        if (wordIds.Count == 0) return new List<UserWordProgress>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var ids = wordIds.ToHashSet();
        var query = db.UserWordProgress
            .Where(p => p.WordId != null && ids.Contains(p.WordId.Value));

        // Drill the whole pool: due-soonest first (new cards — no NextReviewAt — go last).
        var cards = await IncludeOwners(query).ToListAsync(ct);
        return cards
            .OrderBy(p => p.NextReviewAt ?? DateTime.MaxValue)
            .ThenBy(p => p.CreatedAt)
            .ToList();
    }

    public async Task<UserWordProgress> RateAsync(int progressId, SrsRating rating, DateTime now, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        var progress = await db.UserWordProgress.FirstAsync(p => p.Id == progressId, ct);

        var log = _scheduler.Schedule(progress, rating, now);
        log.UserWordProgressId = progress.Id;
        db.ReviewLogs.Add(log);

        await db.SaveChangesAsync(ct);
        _logger.LogDebug("Rated progress {Id} {Rating}: S {SB}→{SA}, D {DB}→{DA}, next +{Days}d",
            progressId, rating, log.StabilityBefore, log.StabilityAfter, log.DifficultyBefore, log.DifficultyAfter, log.ScheduledIntervalDays);
        return progress;
    }

    public async Task<SrsStats> GetStatsAsync(DateTime now, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var total = await db.UserWordProgress.CountAsync(ct);
        var byState = await db.UserWordProgress
            .GroupBy(p => p.State)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int countOf(SrsState s) => byState.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        var dueToday = await db.UserWordProgress
            .CountAsync(p => p.State != SrsState.New && p.NextReviewAt != null && p.NextReviewAt <= now, ct);

        var startOfDay = now.Date;
        var endOfDay = startOfDay.AddDays(1);
        var reviewedToday = await db.ReviewLogs
            .CountAsync(r => r.ReviewedAt >= startOfDay && r.ReviewedAt < endOfDay, ct);
        var lapsesToday = await db.ReviewLogs
            .CountAsync(r => r.ReviewedAt >= startOfDay && r.ReviewedAt < endOfDay && r.Rating == SrsRating.Again, ct);

        var thirtyDaysAgo = now.AddDays(-30);
        var recent = await db.ReviewLogs
            .Where(r => r.ReviewedAt >= thirtyDaysAgo)
            .Select(r => r.Rating)
            .ToListAsync(ct);
        var retention = recent.Count == 0
            ? 0.0
            : (double)recent.Count(x => x != SrsRating.Again) / recent.Count;

        return new SrsStats(
            Total: total,
            New: countOf(SrsState.New),
            Learning: countOf(SrsState.Learning),
            Review: countOf(SrsState.Review),
            Relearning: countOf(SrsState.Relearning),
            DueToday: dueToday,
            ReviewedToday: reviewedToday,
            LapsesToday: lapsesToday,
            RetentionRate30d: retention);
    }

    private static IQueryable<UserWordProgress> IncludeOwners(IQueryable<UserWordProgress> q) =>
        q.Include(p => p.Word).ThenInclude(w => w!.PartOfSpeech)
         .Include(p => p.Word).ThenInclude(w => w!.Senses).ThenInclude(s => s.Translations)
         .Include(p => p.Word).ThenInclude(w => w!.Examples)
         .Include(p => p.PhrasalVerb).ThenInclude(pv => pv!.Senses).ThenInclude(s => s.Translations)
         .Include(p => p.PhrasalVerb).ThenInclude(pv => pv!.Examples)
         .Include(p => p.Collocation);
}
