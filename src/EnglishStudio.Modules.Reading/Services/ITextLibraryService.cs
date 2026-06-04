using EnglishStudio.Modules.Reading.Entities;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>CRUD + import for the user's reading library.</summary>
public interface ITextLibraryService
{
    Task<IReadOnlyList<ReadingTextListItem>> ListAsync(CancellationToken ct = default);

    Task<ReadingTextDetail?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Adds a text: trims, normalizes line endings, computes the word count and a
    /// heuristic CEFR level from dictionary frequency data. Returns the new id.
    /// </summary>
    Task<int> AddAsync(string title, string body, ReadingSource source = ReadingSource.User, CancellationToken ct = default);

    Task RenameAsync(int id, string newTitle, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Soft-hides or restores a text. Used mainly for built-in graded readers, which can't be
    /// deleted (they re-seed on every launch) — hiding keeps the row but drops it from the library.
    /// </summary>
    Task SetHiddenAsync(int id, bool hidden, CancellationToken ct = default);

    /// <summary>Stamps <see cref="ReadingText.LastOpenedAt"/> when a text is opened.</summary>
    Task TouchOpenedAsync(int id, CancellationToken ct = default);
}
