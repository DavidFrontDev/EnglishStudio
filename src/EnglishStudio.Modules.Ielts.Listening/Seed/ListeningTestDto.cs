using System.Text.Json;

namespace EnglishStudio.Modules.Ielts.Listening.Seed;

/// <summary>
/// JSON schema for listening tests embedded in <c>ielts_listening_tests.json</c>.
/// Mirrors the Reading DTO shape (TestSet → Parts → Groups → Questions) with two additions:
/// each part carries an <see cref="ListeningPartDto.AudioFile"/>, and groups use the
/// listening-specific layouts (StructuredNotes / Table / MapLabeling / FlatList).
/// </summary>
public sealed record ListeningTestDto(
    string Code,
    string Title,
    string? Attribution,
    List<ListeningPartDto> Parts)
{
    public bool IsExamOnly { get; init; }
}

public sealed record ListeningPartDto(
    int Order,
    string Title,
    /// <summary>Relative audio file name within the test's content folder, e.g. "audio1.mp3".</summary>
    string? AudioFile,
    string? IntroNoteRu,
    List<ListeningGroupDto> Groups);

public sealed record ListeningGroupDto(
    int Order,
    string Layout,              // "StructuredNotes" | "Table" | "MapLabeling" | "FlatList"
    string? Instruction,
    JsonElement? SharedOptions, // string[] — word box / list of letters (A-H, A-E)
    string? SharedListTitle,
    string? ImagePath,          // relative file name, e.g. "comparison.png" (Comparison cards)
    string? ExampleStem,
    string? ExampleAnswer,
    string? SummaryTemplate,    // StructuredNotes markup OR Table JSON, depending on Layout
    List<ListeningQuestionDto> Questions);

public sealed record ListeningQuestionDto(
    int Order,
    string Type,                // matches QuestionType enum name
    string Stem,
    JsonElement? Options,
    JsonElement AnswerKey,
    JsonElement? AcceptableAnswers,
    int Points = 1,
    int? WordLimitMax = null);
