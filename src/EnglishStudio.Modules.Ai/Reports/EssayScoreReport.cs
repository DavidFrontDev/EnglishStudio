using System.Text.Json.Serialization;

namespace EnglishStudio.Modules.Ai.Reports;

public sealed record EssayScoreReport(
    [property: JsonPropertyName("taskAchievement")] double TaskAchievement,
    [property: JsonPropertyName("coherenceCohesion")] double CoherenceCohesion,
    [property: JsonPropertyName("lexicalResource")] double LexicalResource,
    [property: JsonPropertyName("grammaticalRangeAccuracy")] double GrammaticalRangeAccuracy,
    [property: JsonPropertyName("overall")] double Overall,
    [property: JsonPropertyName("feedbackEn")] string FeedbackEn,
    [property: JsonPropertyName("feedbackRu")] string FeedbackRu,
    [property: JsonPropertyName("issues")] IReadOnlyList<EssayIssue> Issues);

public sealed record EssayIssue(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("explanationRu")] string ExplanationRu,
    [property: JsonPropertyName("explanationEn")] string? ExplanationEn,
    [property: JsonPropertyName("suggestion")] string Suggestion);
