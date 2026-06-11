using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Ai.Reports;

public sealed record SpeakingScoreReport(
    [property: JsonPropertyName("fluencyCoherence")] double FluencyCoherence,
    [property: JsonPropertyName("lexicalResource")] double LexicalResource,
    [property: JsonPropertyName("grammaticalRangeAccuracy")] double GrammaticalRangeAccuracy,
    [property: JsonPropertyName("pronunciation")] double Pronunciation,
    [property: JsonPropertyName("overall")] double Overall,
    [property: JsonPropertyName("feedbackEn")] string FeedbackEn,
    [property: JsonPropertyName("feedbackRu")] string FeedbackRu)
{
    [JsonPropertyName("strengths")]
    public IReadOnlyList<string> Strengths { get; init; } = [];

    [JsonPropertyName("improvements")]
    public IReadOnlyList<string> Improvements { get; init; } = [];
}
