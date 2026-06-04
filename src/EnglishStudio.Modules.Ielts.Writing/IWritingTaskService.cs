using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Writing;

public interface IWritingTaskService
{
    // ---- TestSet-level (full IELTS Writing tests: Task 1 + Task 2) ----

    /// <summary>Lightweight list of all Writing test sets for the hub.</summary>
    Task<IReadOnlyList<WritingTestSetSummary>> ListTestSetsAsync(CancellationToken ct = default);

    /// <summary>Full test set with both tasks and their model answers.</summary>
    Task<WritingTestSetDetail?> GetTestSetAsync(int testSetId, CancellationToken ct = default);

    // ---- Standalone task-level (legacy single-prompt drills) ----

    /// <summary>Lightweight list filtered by Kind for standalone drill mode.</summary>
    Task<IReadOnlyList<WritingTaskSummary>> ListAsync(WritingTaskKind kind, CancellationToken ct = default);

    /// <summary>Full task with its model answers.</summary>
    Task<WritingTask?> GetFullAsync(int taskId, CancellationToken ct = default);

    // ---- Attempts ----

    /// <summary>Start a new attempt for a single task (StartedAt = now, empty text).</summary>
    Task<WritingAttempt> StartAttemptAsync(int taskId, CancellationToken ct = default);

    /// <summary>Persist a draft without finalising the attempt.</summary>
    Task SaveDraftAsync(int attemptId, string userText, CancellationToken ct = default);

    /// <summary>Submit an attempt. Counts words, sets SubmittedAt + DurationSeconds.</summary>
    Task<WritingAttempt> SubmitAttemptAsync(int attemptId, string userText, CancellationToken ct = default);

    /// <summary>Recent attempts, newest first.</summary>
    Task<IReadOnlyList<WritingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Full history with per-criterion bands and test/task metadata for the History screen.
    /// Returns most recent first.
    /// </summary>
    Task<IReadOnlyList<WritingHistoryEntry>> ListHistoryAsync(int limit = 500, CancellationToken ct = default);

    /// <summary>Load a single attempt for the result view.</summary>
    Task<WritingAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default);

    /// <summary>Delete a single attempt (used to discard the empty record left by Cancel).</summary>
    Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default);

    /// <summary>Delete all Writing attempts. Returns the number of rows removed.</summary>
    Task<int> ClearHistoryAsync(CancellationToken ct = default);
}

public sealed record WritingTestSetSummary(
    int Id,
    string Code,
    string Title,
    string? Attribution,
    int Task1Id,
    string Task1PromptPreview,
    WritingChartType Task1ChartType,
    int Task2Id,
    string Task2PromptPreview,
    string? Task2TopicCategory,
    int CompletedAttempts,
    double? LastOverallBand);

public sealed record WritingTestSetDetail(
    TestSet TestSet,
    WritingTask Task1,
    WritingTask Task2);

public sealed record WritingTaskSummary(
    int Id,
    string Code,
    WritingTaskKind Kind,
    string PromptPreview,
    string? TopicCategory,
    WritingChartType ChartType,
    int MinWords,
    int RecommendedMinutes,
    int AttemptCount,
    double? LastBand);

public sealed record WritingAttemptSummary(
    int Id,
    int TaskId,
    string TaskCode,
    WritingTaskKind Kind,
    DateTime StartedAt,
    DateTime? SubmittedAt,
    int WordCount,
    double? BandOverall);

public sealed record WritingHistoryEntry(
    int AttemptId,
    int TaskId,
    string TaskCode,
    string TestSetTitle,
    WritingTaskKind Kind,
    DateTime StartedAt,
    DateTime? SubmittedAt,
    int WordCount,
    int DurationSeconds,
    double? BandTaskAchievement,
    double? BandCoherence,
    double? BandLexical,
    double? BandGrammar,
    double? BandOverall);
