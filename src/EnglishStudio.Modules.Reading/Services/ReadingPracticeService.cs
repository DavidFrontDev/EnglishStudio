using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <inheritdoc cref="IReadingPracticeService"/>
public sealed class ReadingPracticeService : IReadingPracticeService
{
    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly ILogger<ReadingPracticeService> _log;

    public ReadingPracticeService(IDbContextFactory<ReadingDbContext> factory, ILogger<ReadingPracticeService> log)
    {
        _factory = factory;
        _log = log;
    }

    // ── Highlights ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<HighlightDto>> ListHighlightsAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TextHighlights
            .Where(h => h.ReadingTextId == readingTextId)
            .OrderBy(h => h.StartOffset)
            .Select(h => new HighlightDto(h.Id, h.ReadingTextId, h.StartOffset, h.Length, h.Quote, h.Color, h.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> AddHighlightAsync(int readingTextId, int startOffset, int length, string quote, string? color, CancellationToken ct = default)
    {
        if (length <= 0) return 0;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var h = new TextHighlight
        {
            ReadingTextId = readingTextId,
            StartOffset = startOffset,
            Length = length,
            Quote = quote,
            Color = color,
            CreatedAt = DateTime.UtcNow
        };
        db.TextHighlights.Add(h);
        await db.SaveChangesAsync(ct);
        return h.Id;
    }

    public async Task<int> RemoveHighlightsOverlappingAsync(int readingTextId, int startOffset, int length, CancellationToken ct = default)
    {
        var end = startOffset + Math.Max(0, length);
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Intersection of [startOffset, end) with [h.StartOffset, h.StartOffset + h.Length).
        var overlapping = await db.TextHighlights
            .Where(h => h.ReadingTextId == readingTextId
                        && h.StartOffset < end
                        && h.StartOffset + h.Length > startOffset)
            .ToListAsync(ct);

        if (overlapping.Count == 0) return 0;
        db.TextHighlights.RemoveRange(overlapping);
        await db.SaveChangesAsync(ct);
        return overlapping.Count;
    }

    // ── Practice pool ───────────────────────────────────────────────────────

    public async Task<bool> IsInPoolAsync(int readingTextId, int wordId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ReadingPracticeItems.AnyAsync(i => i.ReadingTextId == readingTextId && i.WordId == wordId, ct);
    }

    public async Task AddToPoolAsync(int readingTextId, int wordId, string headword, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.ReadingPracticeItems.AnyAsync(i => i.ReadingTextId == readingTextId && i.WordId == wordId, ct))
            return;

        db.ReadingPracticeItems.Add(new ReadingPracticeItem
        {
            ReadingTextId = readingTextId,
            WordId = wordId,
            Headword = headword,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Added word {WordId} to practice pool of text {TextId}.", wordId, readingTextId);
    }

    public async Task RemoveFromPoolAsync(int readingTextId, int wordId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var item = await db.ReadingPracticeItems
            .FirstOrDefaultAsync(i => i.ReadingTextId == readingTextId && i.WordId == wordId, ct);
        if (item is null) return;
        db.ReadingPracticeItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<int>> ListPoolWordIdsAsync(int readingTextId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ReadingPracticeItems
            .Where(i => i.ReadingTextId == readingTextId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => i.WordId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TextPoolSummary>> ListPoolsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await (
            from t in db.ReadingTexts
            let cnt = db.ReadingPracticeItems.Count(i => i.ReadingTextId == t.Id)
            where cnt > 0
            orderby t.Title
            select new TextPoolSummary(t.Id, t.Title, cnt))
            .ToListAsync(ct);
    }
}
