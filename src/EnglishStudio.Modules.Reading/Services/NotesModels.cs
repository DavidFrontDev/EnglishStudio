namespace EnglishStudio.Modules.Reading.Services;

/// <summary>"Continue from here" bookmark (one per text). <see cref="WordIndex"/> is in the
/// same space as <see cref="TextToken.WordIndex"/>.</summary>
public sealed record BookmarkDto(int Id, int ReadingTextId, int WordIndex, DateTime CreatedAt);

/// <summary>
/// A note attached to a highlighted span. <see cref="StartOffset"/>/<see cref="Length"/> are
/// character offsets into the text's BodyText (same coordinates as <see cref="TextToken"/>).
/// <see cref="Quote"/> is the highlighted excerpt; <see cref="Color"/> is an optional highlight colour.
/// </summary>
public sealed record NoteDto(
    int Id,
    int ReadingTextId,
    int StartOffset,
    int Length,
    string Quote,
    string NoteText,
    string? Color,
    DateTime CreatedAt);
