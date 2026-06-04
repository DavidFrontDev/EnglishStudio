using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// A text the user reads. Stored raw; tokenization (per-word rendering, selection,
/// alignment) happens in memory when the text is opened.
/// </summary>
public class ReadingText
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    public ReadingSource Source { get; set; } = ReadingSource.User;

    /// <summary>Cached word count (computed on add).</summary>
    public int WordCount { get; set; }

    /// <summary>Heuristic difficulty estimated from dictionary frequency/CEFR data.</summary>
    public CefrLevel EstimatedCefr { get; set; } = CefrLevel.Unknown;

    /// <summary>Optional CSV of free-form tags.</summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Soft-hide flag. Built-in graded readers can't be deleted (they re-seed idempotently on every
    /// launch), so the user hides them instead — the row stays, the seeder leaves it alone, and the
    /// library filters it out unless "show hidden" is on.
    /// </summary>
    public bool IsHidden { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastOpenedAt { get; set; }

    public ICollection<ReadingSession> Sessions { get; set; } = new List<ReadingSession>();
}
