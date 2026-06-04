using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Entities;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Lightweight read-model for the library list.</summary>
public sealed record ReadingTextListItem(
    int Id,
    string Title,
    int WordCount,
    CefrLevel EstimatedCefr,
    ReadingSource Source,
    DateTime CreatedAt,
    DateTime? LastOpenedAt,
    double? LastWpm,
    bool IsHidden = false);

/// <summary>Full text for the reader screen.</summary>
public sealed record ReadingTextDetail(
    int Id,
    string Title,
    string BodyText,
    int WordCount,
    CefrLevel EstimatedCefr);
