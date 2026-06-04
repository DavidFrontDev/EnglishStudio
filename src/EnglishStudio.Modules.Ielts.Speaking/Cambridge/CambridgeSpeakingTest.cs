namespace EnglishStudio.Modules.Ielts.Speaking.Cambridge;

/// <summary>One parsed Speaking test from a Cambridge IELTS book.</summary>
public sealed record CambridgeSpeakingTest(
    int Book,
    int TestNumber,
    CambridgePart1 Part1,
    CambridgePart2 Part2,
    CambridgePart3 Part3);

public sealed record CambridgePart1(
    string TopicLabel,
    IReadOnlyList<string> Questions);

public sealed record CambridgePart2(
    string CueCardPrompt,
    IReadOnlyList<string> Subpoints);

public sealed record CambridgePart3(
    IReadOnlyList<CambridgePart3Subtopic> Subtopics);

public sealed record CambridgePart3Subtopic(
    string Label,
    IReadOnlyList<string> Questions);
