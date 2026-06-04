using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class NotesServiceTests
{
    private static int SeedText(InMemoryReadingDb db)
    {
        using var ctx = db.New();
        var t = new ReadingText { Title = "T", BodyText = "the body text", CreatedAt = DateTime.UtcNow };
        ctx.ReadingTexts.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    [Fact]
    public async Task Add_then_list_returns_note()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);

        var id = await svc.AddNoteAsync(textId, startOffset: 4, length: 4, quote: "body", noteText: "a noun", color: "#ffd")
            ;
        var notes = await svc.ListNotesAsync(textId);

        var note = Assert.Single(notes);
        Assert.Equal(id, note.Id);
        Assert.Equal(4, note.StartOffset);
        Assert.Equal("body", note.Quote);
        Assert.Equal("a noun", note.NoteText);
        Assert.Equal("#ffd", note.Color);
    }

    [Fact]
    public async Task Notes_are_ordered_by_offset()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);

        await svc.AddNoteAsync(textId, 9, 4, "text", "second", null);
        await svc.AddNoteAsync(textId, 0, 3, "the", "first", null);

        var notes = await svc.ListNotesAsync(textId);
        Assert.Equal(new[] { "first", "second" }, notes.Select(n => n.NoteText).ToArray());
    }

    [Fact]
    public async Task Update_changes_text_and_color()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);
        var id = await svc.AddNoteAsync(textId, 0, 3, "the", "old", null);

        await svc.UpdateNoteAsync(id, "new note", "#abc");

        var note = Assert.Single(await svc.ListNotesAsync(textId));
        Assert.Equal("new note", note.NoteText);
        Assert.Equal("#abc", note.Color);
    }

    [Fact]
    public async Task Delete_removes_note()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);
        var id = await svc.AddNoteAsync(textId, 0, 3, "the", "x", null);

        await svc.DeleteNoteAsync(id);

        Assert.Empty(await svc.ListNotesAsync(textId));
    }

    [Fact]
    public async Task Bookmark_upserts_and_stays_single()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);

        Assert.Null(await svc.GetBookmarkAsync(textId));

        await svc.SetBookmarkAsync(textId, 5);
        var bm = await svc.GetBookmarkAsync(textId);
        Assert.NotNull(bm);
        Assert.Equal(5, bm!.WordIndex);

        await svc.SetBookmarkAsync(textId, 12); // upsert, not a second row
        bm = await svc.GetBookmarkAsync(textId);
        Assert.Equal(12, bm!.WordIndex);

        using (var ctx = db.New())
            Assert.Equal(1, ctx.TextBookmarks.Count(b => b.ReadingTextId == textId));
    }

    [Fact]
    public async Task Clear_bookmark_removes_it()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var svc = new NotesService(db.Factory);
        await svc.SetBookmarkAsync(textId, 3);

        await svc.ClearBookmarkAsync(textId);

        Assert.Null(await svc.GetBookmarkAsync(textId));
    }
}
