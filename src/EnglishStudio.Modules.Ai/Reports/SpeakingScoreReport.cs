using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Ai.Reports;

public sealed record SpeakingScoreReport(
    [property: JsonPropertyName("fluencyCoherence")] double FluencyCoherence,
    [property: JsonPropertyName("lexicalResource")] double LexicalResource,
    [property: JsonPropertyName("grammaticalRangeAccuracy")] double GrammaticalRangeAccuracy,
    [property: JsonPropertyName("pronunciation")] double Pronunciation,
    [property: JsonPropertyName("overall")] double Overall,
    [property: JsonPropertyName("feedbackEn")] string FeedbackEn,
    [property: JsonPropertyName("feedbackRu")] string FeedbackRu,
    [property: JsonPropertyName("strengths")] IReadOnlyList<string> Strengths,
    [property: JsonPropertyName("improvements")] IReadOnlyList<string> Improvements);
