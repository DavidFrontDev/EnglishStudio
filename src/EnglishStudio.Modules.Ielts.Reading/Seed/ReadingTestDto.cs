using System.Text.Json;

namespace EnglishStudio.Modules.Ielts.Reading.Seed;

/// <summary>
/// JSON schema for reading tests embedded in <c>ielts_reading_tests.json</c>.
/// Tests are organized as TestSet → Parts → Groups → Questions. The generator and
/// <see cref="ReadingSeedService"/> share this exact shape.
/// </summary>
public sealed record ReadingTestDto(
    string Code,
    string Title,
    string Mode,                // "Academic" | "GeneralTraining"
    string? Attribution,
    List<ReadingPartDto> Parts)
{
    /// <summary>
    /// Optional. When true, the test is launched in strict exam mode only:
    /// mandatory timer, no training-mode option, no backward navigation between parts.
    /// </summary>
    public bool IsExamOnly { get; init; }
}

public sealed record ReadingPartDto(
    int Order,
    string Title,
    string Body,                // passage text (700–950 words target)
    string? IntroNoteRu,
    List<ReadingGroupDto> Groups);

public sealed record ReadingGroupDto(
    int Order,
    string Layout,              // "FlatList" | "SummaryFlow" | "MapLabeling"
    string? Instruction,
    JsonElement? SharedOptions, // string[] — list of headings / phrases / word box / places
    string? SharedListTitle,    // "List of headings", "Word box", "List of phrases", "List of places"
    string? ImagePath,          // relative file name within the test's content folder, e.g. "map.png"
    string? ExampleStem,
    string? ExampleAnswer,
    string? SummaryTemplate,    // SummaryFlow only: text with "{n}" placeholders matched to question display numbers
    List<ReadingQuestionDto> Questions);

public sealed record ReadingQuestionDto(
    int Order,
    string Type,                // matches QuestionType enum name (e.g. "TrueFalseNotGiven")
    string Stem,
    JsonElement? Options,
    JsonElement AnswerKey,
    JsonElement? AcceptableAnswers,
    int Points = 1,
    int? WordLimitMax = null);
