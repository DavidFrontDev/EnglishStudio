namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Comprehension questions (F2): generate (once, cached) questions for a text via Claude and
/// grade answers. Implemented by Agent A; consumed by the reader UI (Agent B).
/// </summary>
public interface IComprehensionService
{
    /// <summary>True when Claude CLI is available (generation / open-answer grading).</summary>
    bool CanUseAi { get; }

    /// <summary>
    /// Returns the text's questions, generating + caching them in the DB on first call.
    /// Returns an empty list if there are none cached and AI is unavailable.
    /// </summary>
    Task<IReadOnlyList<ComprehensionQuestionDto>> GetOrGenerateAsync(int textId, CancellationToken ct = default);

    /// <summary>Grades one answer: MCQ locally against the key, Open via Claude.</summary>
    Task<ComprehensionVerdictDto> EvaluateAsync(int questionId, string userAnswer, CancellationToken ct = default);
}
