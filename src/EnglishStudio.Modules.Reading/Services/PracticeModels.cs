namespace EnglishStudio.Modules.Reading.Services;

/// <summary>A persistent colour highlight over a span of a text (char offsets into BodyText).</summary>
public sealed record HighlightDto(
    int Id,
    int ReadingTextId,
    int StartOffset,
    int Length,
    string Quote,
    string? Color,
    DateTime CreatedAt);

/// <summary>A text that has a non-empty practice pool, with its word count (for the trainer list).</summary>
public sealed record TextPoolSummary(int ReadingTextId, string Title, int Count);
