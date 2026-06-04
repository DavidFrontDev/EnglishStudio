using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Listening;

public interface IListeningTestService
{
    Task<IReadOnlyList<ListeningTestSummary>> ListAsync(CancellationToken ct = default);
    Task<TestSet?> GetFullAsync(int testSetId, CancellationToken ct = default);
    Task<IReadOnlyList<ListeningAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default);
    Task<int> CountAttemptsAsync(CancellationToken ct = default);
    Task<TestAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default);
    Task<int> ClearAllAttemptsAsync(CancellationToken ct = default);
}

public sealed record ListeningTestSummary(
    int Id,
    string Code,
    string Title,
    IeltsTestMode Mode,
    int PartCount,
    int QuestionCount,
    double? LastBand,
    bool IsExamOnly);

public sealed record ListeningAttemptSummary(
    int Id,
    int TestSetId,
    string TestTitle,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int RawScore,
    double BandEstimate,
    bool IsTrainingMode);
