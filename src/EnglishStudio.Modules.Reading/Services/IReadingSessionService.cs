namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Persists reading sessions (and per-word stats) and reads history for trends.
/// Implemented by the engine (Agent A) over <c>ReadingDbContext</c>. No new migration —
/// the <c>ReadingSession</c> / <c>ReadingWordStat</c> tables already exist.
/// </summary>
public interface IReadingSessionService
{
    Task<int> SaveAsync(
        int readingTextId,
        DateTime startedAt,
        int durationSec,
        int wordsRead,
        double wpm,
        double accuracyPct,
        bool completed,
        string? audioPath,
        IReadOnlyList<ReadingWordOutcome>? wordStats,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReadingSessionSummary>> ListByTextAsync(int readingTextId, CancellationToken ct = default);

    Task<ReadingSessionSummary?> GetLatestAsync(int readingTextId, CancellationToken ct = default);

    /// <summary>Deletes all reading sessions (and their per-word stats) for a text. Returns the count removed.</summary>
    Task<int> ClearByTextAsync(int readingTextId, CancellationToken ct = default);
}
