using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Dictionary.Srs;

public sealed record SrsStats(
    int Total,
    int New,
    int Learning,
    int Review,
    int Relearning,
    int DueToday,
    int ReviewedToday,
    int LapsesToday,
    double RetentionRate30d);

public interface ISrsService
{
    Task<UserWordProgress?> AddWordAsync(int wordId, CancellationToken ct = default);
    Task<UserWordProgress?> AddPhrasalVerbAsync(int phrasalVerbId, CancellationToken ct = default);
    Task<UserWordProgress?> AddCollocationAsync(int collocationId, CancellationToken ct = default);

    Task<bool> IsInTrainingForWordAsync(int wordId, CancellationToken ct = default);

    /// <summary>
    /// Build the session queue: up to maxReview due cards (NextReviewAt ≤ now) +
    /// up to maxNew brand-new cards (State=New), interleaved.
    /// </summary>
    Task<List<UserWordProgress>> BuildSessionAsync(int maxNew, int maxReview, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Build a focused session over a specific set of words (e.g. a reading text's practice pool):
    /// ALL their cards regardless of due date — due-soonest first, new cards last. Lets the user
    /// drill the whole pool on demand; ratings still feed FSRS.
    /// </summary>
    Task<List<UserWordProgress>> BuildSessionForWordIdsAsync(IReadOnlyCollection<int> wordIds, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Apply the FSRS scheduler to this card, persist progress + ReviewLog.
    /// </summary>
    Task<UserWordProgress> RateAsync(int progressId, SrsRating rating, DateTime now, CancellationToken ct = default);

    Task<SrsStats> GetStatsAsync(DateTime now, CancellationToken ct = default);
}
