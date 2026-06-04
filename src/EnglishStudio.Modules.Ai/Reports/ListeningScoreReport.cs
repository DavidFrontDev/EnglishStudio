using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Ai.Reports;

/// <summary>
/// AI-generated explanation report for a completed IELTS Listening attempt.
/// Mirrors the shape of <see cref="EssayScoreReport"/> but split per-question instead
/// of per-criterion, since Listening grading itself is mechanical (auto-checker counts
/// matches against the answer key) — the value an AI adds is "why did I miss Q14",
/// "I keep failing Map labeling — here's what to listen for", etc.
/// </summary>
public sealed record ListeningScoreReport(
    /// <summary>Plain-text overall summary in Russian (3-6 sentences). Highlights strongest part(s), weakest part/type, and the single most actionable change.</summary>
    [property: JsonPropertyName("summaryRu")] string SummaryRu,

    /// <summary>Same overall summary in English.</summary>
    [property: JsonPropertyName("summaryEn")] string SummaryEn,

    /// <summary>Per-part diagnostics (Part 1..4) — what kind of mistakes the listener tends to make in this section.</summary>
    [property: JsonPropertyName("partInsights")] IReadOnlyList<PartInsight> PartInsights,

    /// <summary>Per-question explanations for the wrong answers. Only filled for questions the user missed; correct ones are omitted to keep the prompt focused.</summary>
    [property: JsonPropertyName("questionExplanations")] IReadOnlyList<QuestionExplanation> QuestionExplanations,

    /// <summary>3-5 prioritised study tips in Russian — concrete habits / drills to fix the listener's weakest area.</summary>
    [property: JsonPropertyName("tipsRu")] IReadOnlyList<string> TipsRu);

public sealed record PartInsight(
    [property: JsonPropertyName("partNumber")] int PartNumber,
    [property: JsonPropertyName("partTitle")] string PartTitle,
    /// <summary>1-2 sentence diagnosis of what went wrong (or right) in this part.</summary>
    [property: JsonPropertyName("commentRu")] string CommentRu);

public sealed record QuestionExplanation(
    [property: JsonPropertyName("questionNumber")] int QuestionNumber,
    [property: JsonPropertyName("userAnswer")] string UserAnswer,
    [property: JsonPropertyName("correctAnswer")] string CorrectAnswer,
    /// <summary>Why this answer was correct, citing the exact phrase in the transcript when possible.</summary>
    [property: JsonPropertyName("explanationRu")] string ExplanationRu);
