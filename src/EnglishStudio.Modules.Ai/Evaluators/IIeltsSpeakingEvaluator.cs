using EnglishStudio.Modules.Ai.Reports;

namespace EnglishStudio.Modules.Ai.Evaluators;

public enum SpeakingPartType
{
    Part1 = 1,
    Part2 = 2,
    Part3 = 3
}

public sealed record SpeakingTurn(
    string Question,
    string UserTranscript,
    int DurationSeconds,
    string? ModelAnswer = null);

public sealed record SpeakingMetrics(
    double WordsPerMinute,
    double PauseRatio,
    int FillerCount,
    double TypeTokenRatio);

public interface IIeltsSpeakingEvaluator
{
    Task<SpeakingScoreReport?> EvaluateAsync(
        SpeakingPartType partType,
        string? topic,
        IReadOnlyList<SpeakingTurn> turns,
        SpeakingMetrics metrics,
        CancellationToken ct = default);
}
