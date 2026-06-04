using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Notes &amp; bookmarks (F5) over <see cref="ReadingDbContext"/> via
/// <see cref="IDbContextFactory{ReadingDbContext}"/>. The bookmark is upserted (one per text).
/// </summary>
public sealed class NotesService : INotesService
{
    private readonly IDbContextFactory<ReadingDbContext> _factory;

    public NotesService(IDbContextFactory<ReadingDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<NoteDto>> ListNotesAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TextNotes
            .Where(n => n.ReadingTextId == readingTextId)
            .OrderBy(n => n.StartOffset)
            .Select(n => new NoteDto(n.Id, n.ReadingTextId, n.StartOffset, n.Length, n.Quote, n.NoteText, n.Color, n.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> AddNoteAsync(
        int readingTextId, int startOffset, int length, string quote, string noteText, string? color,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var note = new TextNote
        {
            ReadingTextId = readingTextId,
            StartOffset = startOffset,
            Length = length,
            Quote = quote ?? string.Empty,
            NoteText = noteText ?? string.Empty,
            Color = color,
            CreatedAt = DateTime.UtcNow
        };
        db.TextNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return note.Id;
    }

    public async Task UpdateNoteAsync(int noteId, string noteText, string? color, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var note = await db.TextNotes.FindAsync([noteId], ct);
        if (note is null) return;
        note.NoteText = noteText ?? string.Empty;
        note.Color = color;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteNoteAsync(int noteId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var note = await db.TextNotes.FindAsync([noteId], ct);
        if (note is null) return;
        db.TextNotes.Remove(note);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BookmarkDto?> GetBookmarkAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TextBookmarks
            .Where(b => b.ReadingTextId == readingTextId)
            .Select(b => new BookmarkDto(b.Id, b.ReadingTextId, b.WordIndex, b.CreatedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetBookmarkAsync(int readingTextId, int wordIndex, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.TextBookmarks.FirstOrDefaultAsync(b => b.ReadingTextId == readingTextId, ct);
        if (existing is null)
        {
            db.TextBookmarks.Add(new TextBookmark
            {
                ReadingTextId = readingTextId,
                WordIndex = wordIndex,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.WordIndex = wordIndex;
            existing.CreatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearBookmarkAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.TextBookmarks.Where(b => b.ReadingTextId == readingTextId).ToListAsync(ct);
        if (existing.Count == 0) return;
        db.TextBookmarks.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
    }
}
