using EnglishStudio.Modules.Ai.Reports;

namespace EnglishStudio.Modules.Ielts.Speaking;

public interface ISpeakingFeedbackService
{
    /// <summary>
    /// Loads the attempt, partitions responses by Speaking Part, asks the AI evaluator to
    /// score each Part, and persists the four-criterion bands + a serialised feedback JSON
    /// back to the attempt. Returns the aggregated overall report or null if the CLI is
    /// unavailable / response unparseable for every part.
    /// </summary>
    Task<SpeakingScoreReport?> EvaluateAndSaveAsync(int attemptId, CancellationToken ct = default);
}
