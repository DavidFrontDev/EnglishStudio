using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using EnglishStudio.Modules.Ielts.Listening;
using EnglishStudio.Modules.Ielts.Reading;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;

namespace EnglishStudio.Integration.Tests.Infrastructure;

// Лёгкие ручные двойники секционных сервисов. Реализован только тот метод, который реально дёргает
// тестируемый код (picker), остальное бросает — так тест громко падает, если оркестратор внезапно
// обратится к секционному сервису (в FinaliseAsync с закэшированными band'ами он не должен).

public sealed class FakeSpeakingTestService(IReadOnlyList<SpeakingTopicSummary> part2Topics) : ISpeakingTestService
{
    public Task<IReadOnlyList<SpeakingTopicSummary>> ListTopicsAsync(SpeakingPart part, CancellationToken ct = default)
        => Task.FromResult(part == SpeakingPart.Part2 ? part2Topics : Array.Empty<SpeakingTopicSummary>());

    public Task<IReadOnlyList<SpeakingQuestionDetail>> GetQuestionsForBankAsync(int bankId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SpeakingTopicSummary?> PickRandomTopicAsync(SpeakingPart part, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FullMockBundle> StartFullMockAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FullMockBundle> StartFullMockAsync(int part2BankId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> StartAttemptAsync(SpeakingMode mode, int? topicBankId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SaveResponseAsync(int attemptId, int questionId, string audioPath, string? transcript, int durationSeconds, IReadOnlyList<SpokenWord>? words = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task FinishAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<SpeakingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SpeakingAttemptDetail?> GetAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ClearHistoryAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class FakeListeningTestService(IReadOnlyList<ListeningTestSummary> tests) : IListeningTestService
{
    public Task<IReadOnlyList<ListeningTestSummary>> ListAsync(CancellationToken ct = default) => Task.FromResult(tests);

    public Task<TestSet?> GetFullAsync(int testSetId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ListeningAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> CountAttemptsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TestAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ClearAllAttemptsAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class FakeReadingTestService(IReadOnlyList<ReadingTestSummary> tests) : IReadingTestService
{
    public Task<IReadOnlyList<ReadingTestSummary>> ListAsync(CancellationToken ct = default) => Task.FromResult(tests);

    public Task<TestSet?> GetFullAsync(int testSetId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ReadingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> CountAttemptsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TestAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ClearAllAttemptsAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

public sealed class FakeWritingTaskService(IReadOnlyList<WritingTestSetSummary> testSets) : IWritingTaskService
{
    public Task<IReadOnlyList<WritingTestSetSummary>> ListTestSetsAsync(CancellationToken ct = default) => Task.FromResult(testSets);

    public Task<WritingTestSetDetail?> GetTestSetAsync(int testSetId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<WritingTaskSummary>> ListAsync(WritingTaskKind kind, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WritingTask?> GetFullAsync(int taskId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WritingAttempt> StartAttemptAsync(int taskId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SaveDraftAsync(int attemptId, string userText, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WritingAttempt> SubmitAttemptAsync(int attemptId, string userText, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<WritingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<WritingHistoryEntry>> ListHistoryAsync(int limit = 500, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WritingAttempt?> GetAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> ClearHistoryAsync(CancellationToken ct = default) => throw new NotImplementedException();
}

/// <summary>Никогда не должен вызываться в тестах FinaliseAsync с закэшированными band'ами.</summary>
public sealed class FakeTestRunner : ITestRunner
{
    public Task<TestAttempt> StartAsync(int testSetId, bool trainingMode, CancellationToken ct = default) => throw new NotImplementedException();
    public Task SubmitAnswerAsync(int attemptId, int questionId, string userAnswerJson, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TestAttempt> FinishAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<TestAttempt?> GetAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task AbandonAsync(int attemptId, CancellationToken ct = default) => throw new NotImplementedException();
}
