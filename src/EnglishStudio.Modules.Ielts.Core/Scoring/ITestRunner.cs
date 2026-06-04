using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Shared lifecycle for any Reading/Listening attempt (timer, navigation, answer save, finish).
/// Section-specific orchestration (audio playback, passage layout, etc.) lives in module ViewModels.
/// </summary>
public interface ITestRunner
{
    Task<TestAttempt> StartAsync(int testSetId, bool trainingMode, CancellationToken ct = default);
    Task SubmitAnswerAsync(int attemptId, int questionId, string userAnswerJson, CancellationToken ct = default);
    Task<TestAttempt> FinishAsync(int attemptId, CancellationToken ct = default);
    Task<TestAttempt?> GetAsync(int attemptId, CancellationToken ct = default);

    /// <summary>Removes an in-progress (not yet finished) attempt so it does not pollute history.</summary>
    Task AbandonAsync(int attemptId, CancellationToken ct = default);
}
