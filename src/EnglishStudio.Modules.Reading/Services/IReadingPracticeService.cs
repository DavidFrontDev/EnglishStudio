namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Per-text reading "practice": persistent colour highlights and the word pool the user builds from
/// the reader ("🖍 Выделить" / "🎯 В тренировку"). The pool stores dictionary word ids so the trainer
/// can run a text-scoped FSRS session. Implemented over <c>ReadingDbContext</c>.
/// </summary>
public interface IReadingPracticeService
{
    // ── Highlights ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<HighlightDto>> ListHighlightsAsync(int readingTextId, CancellationToken ct = default);

    Task<int> AddHighlightAsync(int readingTextId, int startOffset, int length, string quote, string? color, CancellationToken ct = default);

    /// <summary>Removes every highlight whose span intersects [startOffset, startOffset+length). Returns the count removed.</summary>
    Task<int> RemoveHighlightsOverlappingAsync(int readingTextId, int startOffset, int length, CancellationToken ct = default);

    // ── Practice pool ───────────────────────────────────────────────────────
    Task<bool> IsInPoolAsync(int readingTextId, int wordId, CancellationToken ct = default);

    /// <summary>Adds (idempotently) a word to the text's pool.</summary>
    Task AddToPoolAsync(int readingTextId, int wordId, string headword, CancellationToken ct = default);

    Task RemoveFromPoolAsync(int readingTextId, int wordId, CancellationToken ct = default);

    Task<IReadOnlyList<int>> ListPoolWordIdsAsync(int readingTextId, CancellationToken ct = default);

    /// <summary>Texts that have a non-empty pool, with their word counts (for the trainer's pool list).</summary>
    Task<IReadOnlyList<TextPoolSummary>> ListPoolsAsync(CancellationToken ct = default);
}
