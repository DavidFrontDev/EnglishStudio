namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Tuning for pagination of a long text.</summary>
public sealed record PaginationOptions(int TargetWordsPerPage = 1500, bool DetectChapters = true);

/// <summary>
/// One page of a text. Ranges are in GLOBAL text coordinates (both <see cref="TextToken.WordIndex"/>
/// space and character offsets) so notes, bookmarks and read-along keep mapping across pages.
/// <see cref="Heading"/> is the chapter title when detected, otherwise null.
/// </summary>
public sealed record TextPage(
    int Index,
    int StartWordIndex,
    int EndWordIndex,
    int StartCharOffset,
    int EndCharOffset,
    string? Heading);
