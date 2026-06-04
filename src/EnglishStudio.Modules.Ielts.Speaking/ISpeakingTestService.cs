namespace EnglishStudio.Modules.Ielts.Speaking;

public enum SpeakingPart { Part1, Part2, Part3 }

public enum SpeakingMode { FullMock, Part1Only, Part2Only, Part3Only }

public sealed record SpeakingTopicSummary(
    int BankId,
    SpeakingPart Part,
    string TopicCode,
    string TopicLabel,
    string? CueCardPrompt,
    int QuestionCount,
    IReadOnlyList<string>? CueCardSubpoints = null);

public sealed record SpeakingQuestionDetail(
    int QuestionId,
    int BankId,
    int OrderInBank,
    string Text,
    int? FollowUpToQuestionId);

public sealed record SpeakingAttemptSummary(
    int AttemptId,
    SpeakingMode Mode,
    DateTime StartedAt,
    DateTime? FinishedAt,
    double? BandOverall);

public sealed record FullMockBundle(
    IReadOnlyList<SpeakingQuestionDetail> Part1Questions,
    SpeakingTopicSummary Part2Topic,
    SpeakingQuestionDetail Part2Question,
    IReadOnlyList<SpeakingQuestionDetail> Part3FollowUps);

public sealed record SpeakingResponseDetail(
    int Id,
    int QuestionId,
    string QuestionText,
    string AudioPath,
    string? Transcript,
    int DurationSeconds,
    double? WpmRate,
    double? PauseRatio,
    int? FillerCount,
    double? TypeTokenRatio);

public sealed record SpeakingAttemptDetail(
    SpeakingAttemptSummary Summary,
    IReadOnlyList<SpeakingResponseDetail> Responses,
    double? BandFluencyCoherence,
    double? BandLexicalResource,
    double? BandGrammar,
    double? BandPronunciation,
    string? FeedbackJson);

public interface ISpeakingTestService
{
    Task<IReadOnlyList<SpeakingTopicSummary>> ListTopicsAsync(SpeakingPart part, CancellationToken ct = default);
    Task<IReadOnlyList<SpeakingQuestionDetail>> GetQuestionsForBankAsync(int bankId, CancellationToken ct = default);
    Task<SpeakingTopicSummary?> PickRandomTopicAsync(SpeakingPart part, CancellationToken ct = default);

    /// <summary>Случайный full mock (Part2 — случайный банк).</summary>
    Task<FullMockBundle> StartFullMockAsync(CancellationToken ct = default);

    /// <summary>
    /// Full mock, выровненный по конкретному Cambridge-тесту: Part2 = банк <paramref name="part2BankId"/>,
    /// Part1 = банк того же теста (<c>cambridge-{book}-test-{n}-part-1</c>), Part3 = банк со
    /// <c>LinkedPart2BankId = part2BankId</c>. Если банк не найден/пуст — деградирует к случайному
    /// набору (<see cref="StartFullMockAsync(CancellationToken)"/>).
    /// </summary>
    Task<FullMockBundle> StartFullMockAsync(int part2BankId, CancellationToken ct = default);

    Task<int> StartAttemptAsync(SpeakingMode mode, int? topicBankId, CancellationToken ct = default);
    Task SaveResponseAsync(int attemptId, int questionId, string audioPath, string? transcript,
        int durationSeconds, IReadOnlyList<SpokenWord>? words = null, CancellationToken ct = default);
    Task FinishAttemptAsync(int attemptId, CancellationToken ct = default);

    Task<IReadOnlyList<SpeakingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default);
    Task<SpeakingAttemptDetail?> GetAttemptAsync(int attemptId, CancellationToken ct = default);
    Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default);
    Task<int> ClearHistoryAsync(CancellationToken ct = default);
}
