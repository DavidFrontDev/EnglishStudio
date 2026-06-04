using System.Linq;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>Per-text practice pool + colour highlights (ReadingPracticeService).</summary>
public class ReadingPracticeServiceTests
{
    private static ReadingPracticeService Make(InMemoryReadingDb db) =>
        new(db.Factory, NullLogger<ReadingPracticeService>.Instance);

    private static int AddText(InMemoryReadingDb db, string title)
    {
        using var ctx = db.New();
        var t = new ReadingText { Title = title, BodyText = "body", Source = ReadingSource.User, CreatedAt = DateTime.UtcNow };
        ctx.ReadingTexts.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    // ── Pool ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pool_add_is_idempotent_and_listed_by_text()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        var t1 = AddText(db, "First");
        var t2 = AddText(db, "Second");

        await svc.AddToPoolAsync(t1, wordId: 10, "alpha");
        await svc.AddToPoolAsync(t1, wordId: 10, "alpha");   // duplicate ignored
        await svc.AddToPoolAsync(t1, wordId: 11, "beta");
        await svc.AddToPoolAsync(t2, wordId: 99, "gamma");

        Assert.True(await svc.IsInPoolAsync(t1, 10));
        Assert.False(await svc.IsInPoolAsync(t1, 99));

        var ids1 = await svc.ListPoolWordIdsAsync(t1);
        Assert.Equal(new[] { 10, 11 }, ids1.OrderBy(x => x).ToArray());
        Assert.Single(await svc.ListPoolWordIdsAsync(t2));
    }

    [Fact]
    public async Task Pool_remove_and_pools_listing_with_counts()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        var t1 = AddText(db, "Beta text");
        var t2 = AddText(db, "Alpha text");
        AddText(db, "Empty text");   // has no pool → excluded from ListPools

        await svc.AddToPoolAsync(t1, 1, "a");
        await svc.AddToPoolAsync(t1, 2, "b");
        await svc.AddToPoolAsync(t2, 3, "c");

        var pools = await svc.ListPoolsAsync();
        Assert.Equal(2, pools.Count);                       // empty text excluded
        Assert.Equal("Alpha text", pools[0].Title);          // ordered by title
        Assert.Equal(2, pools.First(p => p.ReadingTextId == t1).Count);

        await svc.RemoveFromPoolAsync(t1, 1);
        Assert.False(await svc.IsInPoolAsync(t1, 1));
        Assert.Single(await svc.ListPoolWordIdsAsync(t1));
    }

    // ── Highlights ────────────────────────────────────────────────────────

    [Fact]
    public async Task Highlight_add_and_list()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        var t = AddText(db, "T");

        await svc.AddHighlightAsync(t, startOffset: 10, length: 5, "hello", "#7C4DFF");
        await svc.AddHighlightAsync(t, startOffset: 30, length: 4, "word", "#7C4DFF");

        var list = await svc.ListHighlightsAsync(t);
        Assert.Equal(2, list.Count);
        Assert.Equal(10, list[0].StartOffset);              // ordered by offset
        Assert.Equal("#7C4DFF", list[0].Color);
    }

    [Fact]
    public async Task RemoveHighlightsOverlapping_only_removes_intersecting()
    {
        using var db = new InMemoryReadingDb();
        var svc = Make(db);
        var t = AddText(db, "T");
        await svc.AddHighlightAsync(t, 10, 5, "a", null);    // [10,15)
        await svc.AddHighlightAsync(t, 30, 5, "b", null);    // [30,35)

        // Selection [12,20) intersects only the first.
        var removed = await svc.RemoveHighlightsOverlappingAsync(t, startOffset: 12, length: 8);
        Assert.Equal(1, removed);

        var left = await svc.ListHighlightsAsync(t);
        Assert.Single(left);
        Assert.Equal(30, left[0].StartOffset);

        // A non-overlapping selection removes nothing.
        Assert.Equal(0, await svc.RemoveHighlightsOverlappingAsync(t, 0, 5));
    }
}
