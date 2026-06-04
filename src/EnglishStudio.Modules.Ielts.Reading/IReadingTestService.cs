using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Reading;

public interface IReadingTestService
{
    /// <summary>Lightweight list for the hub view.</summary>
    Task<IReadOnlyList<ReadingTestSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Load a Reading test in full (TestSet + Parts + Questions) for the test view.</summary>
    Task<TestSet?> GetFullAsync(int testSetId, CancellationToken ct = default);

    /// <summary>Recent Reading attempts, newest first.</summary>
    Task<IReadOnlyList<ReadingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>Total number of Reading attempts in the database (used by the clear-history confirmation).</summary>
    Task<int> CountAttemptsAsync(CancellationToken ct = default);

    /// <summary>Load full attempt with answers (for result view).</summary>
    Task<TestAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default);

    /// <summary>
    /// Deletes every Reading attempt (and its answers) from the database. Returns the number of attempts removed.
    /// </summary>
    Task<int> ClearAllAttemptsAsync(CancellationToken ct = default);
}

public sealed record ReadingTestSummary(
    int Id,
    string Code,
    string Title,
    IeltsTestMode Mode,
    int PartCount,
    int QuestionCount,
    double? LastBand,
    bool IsExamOnly);

public sealed record ReadingAttemptSummary(
    int Id,
    int TestSetId,
    string TestTitle,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int RawScore,
    double BandEstimate,
    bool IsTrainingMode);
