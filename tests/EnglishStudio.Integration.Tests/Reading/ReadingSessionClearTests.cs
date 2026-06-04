using System.Linq;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>Clearing a text's reading history removes its sessions (and per-word stats) only.</summary>
public class ReadingSessionClearTests
{
    private static ReadingSessionService Make(InMemoryReadingDb db) =>
        new(db.Factory, NullLogger<ReadingSessionService>.Instance);

    /// <summary>Inserts a parent text (sessions FK-reference it) and returns its id.</summary>
    private static int AddText(InMemoryReadingDb db, string title)
    {
        using var ctx = db.New();
        var t = new ReadingText { Title = title, BodyText = "body", Source = ReadingSource.User, CreatedAt = DateTime.UtcNow };
        ctx.ReadingTexts.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    private static Task SaveOne(ReadingSessionService svc, int textId, double wpm) =>
        svc.SaveAsync(
            textId, DateTime.UtcNow, durationSec: 60, wordsRead: 100, wpm: wpm,
            accuracyPct: 90, completed: true, audioPath: null,
            wordStats: new[] { new ReadingWordOutcome(0, true, false, false, 0.9) });

    [Fact]
    public async Task ClearByTextAsync_removes_only_that_texts_sessions()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        var text1 = AddText(db, "First");
        var text2 = AddText(db, "Second");

        await SaveOne(svc, text1, wpm: 120);
        await SaveOne(svc, text1, wpm: 130);
        await SaveOne(svc, text2, wpm: 140);

        var removed = await svc.ClearByTextAsync(text1);

        Assert.Equal(2, removed);
        Assert.Empty(await svc.ListByTextAsync(text1));
        Assert.Single(await svc.ListByTextAsync(text2));

        // Word stats of the cleared sessions are gone; the surviving text's remain.
        using var ctx = db.New();
        Assert.Equal(1, ctx.ReadingWordStats.Count());
    }

    [Fact]
    public async Task ClearByTextAsync_on_empty_history_is_a_noop()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        Assert.Equal(0, await svc.ClearByTextAsync(99));
    }
}
