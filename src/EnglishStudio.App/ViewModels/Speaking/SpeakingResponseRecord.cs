using EnglishStudio.Modules.Ielts.Speaking;

namespace EnglishStudio.App.ViewModels.Speaking;

/// <summary>One captured answer inside a running session: enough to persist via the service.</summary>
public sealed record SpeakingResponseRecord(
    SpeakingPart Part,
    int QuestionId,
    string QuestionText,
    string AudioPath,
    string? Transcript,
    int DurationSeconds,
    IReadOnlyList<SpokenWord>? Words = null);
