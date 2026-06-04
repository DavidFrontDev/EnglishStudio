using EnglishStudio.Modules.Ai.Reports;

namespace EnglishStudio.Modules.Ai.Evaluators;

public enum WritingTaskType
{
    Task1Academic = 1,
    Task1GeneralTraining = 2,
    Task2 = 3
}

/// <summary>
/// A scored sample response provided to the evaluator as a calibration anchor.
/// The evaluator should mirror the band level the examiner gave when grading similar work.
/// </summary>
public sealed record EssayReferenceExample(
    int BandLevel,
    string AnswerText,
    string? ExaminerComment);

public interface IIeltsEssayEvaluator
{
    /// <summary>
    /// Score an IELTS Writing essay through the Claude CLI. Returns null if CLI is unavailable
    /// or the response could not be parsed.
    /// </summary>
    /// <param name="referenceExamples">
    /// Optional scored sample responses for this exact prompt. When supplied, the evaluator
    /// includes them in the system prompt as calibration anchors so its band scores stay
    /// consistent with the examiner-published baseline.
    /// </param>
    /// <param name="taskImagePath">
    /// Optional absolute path to the chart/map/diagram image for Academic Task 1.
    /// When provided and the file exists, the evaluator attaches the image so Claude can
    /// verify factual claims (data points, labels, trends) against the visual itself.
    /// Null for Task 2 / GT letters.
    /// </param>
    Task<EssayScoreReport?> EvaluateAsync(
        WritingTaskType taskType,
        string prompt,
        string userEssay,
        IReadOnlyList<EssayReferenceExample>? referenceExamples = null,
        string? taskImagePath = null,
        CancellationToken ct = default);
}
