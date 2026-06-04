using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Persists reading sessions (and per-word stats) and reads history for trends.
/// Uses <see cref="IDbContextFactory{ReadingDbContext}"/> like <c>TextLibraryService</c>;
/// no new migration — the <c>ReadingSession</c> / <c>ReadingWordStat</c> tables already exist.
/// </summary>
public sealed class ReadingSessionService : IReadingSessionService
{
    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly ILogger<ReadingSessionService> _log;

    public ReadingSessionService(IDbContextFactory<ReadingDbContext> factory, ILogger<ReadingSessionService> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<int> SaveAsync(
        int readingTextId,
        DateTime startedAt,
        int durationSec,
        int wordsRead,
        double wpm,
        double accuracyPct,
        bool completed,
        string? audioPath,
        IReadOnlyList<ReadingWordOutcome>? wordStats,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = new ReadingSession
        {
            ReadingTextId = readingTextId,
            StartedAt = startedAt,
            DurationSec = durationSec,
            WordsRead = wordsRead,
            Wpm = wpm,
            AccuracyPct = accuracyPct,
            Completed = completed,
            AudioPath = audioPath
        };

        if (wordStats is { Count: > 0 })
        {
            foreach (var w in wordStats)
            {
                session.WordStats.Add(new ReadingWordStat
                {
                    TokenIndex = w.TokenIndex,
                    Skipped = w.Skipped,
                    Mispronounced = w.Mispronounced,
                    Score = w.Score
                });
            }
        }

        db.ReadingSessions.Add(session);
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Saved reading session {Id} for text {TextId} ({Words} words, {Wpm:0} wpm).",
            session.Id, readingTextId, wordsRead, wpm);
        return session.Id;
    }

    public async Task<IReadOnlyList<ReadingSessionSummary>> ListByTextAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.ReadingSessions
            .Where(s => s.ReadingTextId == readingTextId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new ReadingSessionSummary(
                s.Id, s.ReadingTextId, s.StartedAt, s.DurationSec,
                s.WordsRead, s.Wpm, s.AccuracyPct, s.Completed))
            .ToListAsync(ct);
    }

    public async Task<int> ClearByTextAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Load with WordStats so the delete is fully explicit (not reliant on the SQLite FK pragma).
        var sessions = await db.ReadingSessions
            .Where(s => s.ReadingTextId == readingTextId)
            .Include(s => s.WordStats)
            .ToListAsync(ct);

        if (sessions.Count == 0) return 0;

        db.ReadingSessions.RemoveRange(sessions);
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Cleared {Count} reading session(s) for text {TextId}.", sessions.Count, readingTextId);
        return sessions.Count;
    }

    public async Task<ReadingSessionSummary?> GetLatestAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.ReadingSessions
            .Where(s => s.ReadingTextId == readingTextId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new ReadingSessionSummary(
                s.Id, s.ReadingTextId, s.StartedAt, s.DurationSec,
                s.WordsRead, s.Wpm, s.AccuracyPct, s.Completed))
            .FirstOrDefaultAsync(ct);
    }
}
