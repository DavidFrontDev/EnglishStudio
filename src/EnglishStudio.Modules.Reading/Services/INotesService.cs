namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Notes &amp; bookmarks (F5): highlighted-span notes and a per-text "continue from here"
/// bookmark. Implemented by Agent A; consumed by the reader UI (Agent B).
/// </summary>
public interface INotesService
{
    Task<IReadOnlyList<NoteDto>> ListNotesAsync(int readingTextId, CancellationToken ct = default);

    Task<int> AddNoteAsync(
        int readingTextId, int startOffset, int length, string quote, string noteText, string? color,
        CancellationToken ct = default);

    Task UpdateNoteAsync(int noteId, string noteText, string? color, CancellationToken ct = default);

    Task DeleteNoteAsync(int noteId, CancellationToken ct = default);

    Task<BookmarkDto?> GetBookmarkAsync(int readingTextId, CancellationToken ct = default);

    /// <summary>Sets (upserts) the text's bookmark to <paramref name="wordIndex"/>.</summary>
    Task SetBookmarkAsync(int readingTextId, int wordIndex, CancellationToken ct = default);

    Task ClearBookmarkAsync(int readingTextId, CancellationToken ct = default);
}
