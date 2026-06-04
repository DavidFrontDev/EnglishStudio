using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

public sealed class TextLibraryService : ITextLibraryService
{
    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GradedReaderSeedService _seeder;
    private readonly ILogger<TextLibraryService> _log;

    public TextLibraryService(
        IDbContextFactory<ReadingDbContext> factory,
        IServiceScopeFactory scopeFactory,
        GradedReaderSeedService seeder,
        ILogger<TextLibraryService> log)
    {
        _factory = factory;
        _scopeFactory = scopeFactory;
        _seeder = seeder;
        _log = log;
    }

    public async Task<IReadOnlyList<ReadingTextListItem>> ListAsync(CancellationToken ct = default)
    {
        // Lazily install built-in graded readers (F7) on first listing — idempotent, no App change.
        try { await _seeder.EnsureSeededAsync(ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Graded-reader seeding failed; continuing."); }

        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.ReadingTexts
            .OrderByDescending(t => t.LastOpenedAt ?? t.CreatedAt)
            .Select(t => new ReadingTextListItem(
                t.Id,
                t.Title,
                t.WordCount,
                t.EstimatedCefr,
                t.Source,
                t.CreatedAt,
                t.LastOpenedAt,
                t.Sessions
                    .Where(s => s.Completed)
                    .OrderByDescending(s => s.StartedAt)
                    .Select(s => (double?)s.Wpm)
                    .FirstOrDefault(),
                t.IsHidden))
            .ToListAsync(ct);
    }

    public async Task<ReadingTextDetail?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        return await db.ReadingTexts
            .Where(t => t.Id == id)
            .Select(t => new ReadingTextDetail(t.Id, t.Title, t.BodyText, t.WordCount, t.EstimatedCefr))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> AddAsync(string title, string body, ReadingSource source = ReadingSource.User, CancellationToken ct = default)
    {
        body = NormalizeNewlines(body).Trim();
        title = string.IsNullOrWhiteSpace(title) ? "Без названия" : title.Trim();

        var entity = new ReadingText
        {
            Title = title,
            BodyText = body,
            Source = source,
            WordCount = ReadingTokenizer.CountWords(body),
            EstimatedCefr = await EstimateCefrAsync(body, ct),
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ReadingTexts.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task RenameAsync(int id, string newTitle, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newTitle)) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.ReadingTexts.FindAsync([id], ct);
        if (entity is null) return;
        entity.Title = newTitle.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.ReadingTexts.FindAsync([id], ct);
        if (entity is null) return;
        db.ReadingTexts.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetHiddenAsync(int id, bool hidden, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.ReadingTexts.FindAsync([id], ct);
        if (entity is null) return;
        entity.IsHidden = hidden;
        await db.SaveChangesAsync(ct);
    }

    public async Task TouchOpenedAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entity = await db.ReadingTexts.FindAsync([id], ct);
        if (entity is null) return;
        entity.LastOpenedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Heuristic difficulty: looks up each distinct word in the dictionary, takes the
    /// 80th percentile of known CEFR levels (a text is gated by its harder vocabulary,
    /// not its average), and bumps the result up when many words fall outside the
    /// dictionary. Returns <see cref="CefrLevel.Unknown"/> when there's no signal.
    /// </summary>
    private async Task<CefrLevel> EstimateCefrAsync(string body, CancellationToken ct)
    {
        var distinct = ReadingTokenizer.Tokenize(body)
            .Where(t => t.Kind == TokenKind.Word)
            .Select(t => ReadingTokenizer.NormalizeWord(t.Text))
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (distinct.Count == 0) return CefrLevel.Unknown;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dict = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

            var levels = new List<int>();
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Chunk to stay well under SQLite's parameter limit.
            foreach (var chunk in Chunk(distinct, 400))
            {
                var rows = await dict.Words
                    .Where(w => chunk.Contains(w.Lemma) || chunk.Contains(w.Headword))
                    .Select(w => new { w.Lemma, w.Headword, w.CefrLevel })
                    .ToListAsync(ct);

                foreach (var r in rows)
                {
                    matched.Add(r.Lemma);
                    matched.Add(r.Headword);
                    if (r.CefrLevel != CefrLevel.Unknown)
                        levels.Add((int)r.CefrLevel);
                }
            }

            if (levels.Count == 0) return CefrLevel.Unknown;

            levels.Sort();
            var p80 = Percentile(levels, 0.80);

            var unmatched = distinct.Count(w => !matched.Contains(w));
            var unknownRatio = (double)unmatched / distinct.Count;
            if (unknownRatio > 0.25) p80 = Math.Min(p80 + 1, (int)CefrLevel.C2);

            return (CefrLevel)Math.Clamp(p80, (int)CefrLevel.A1, (int)CefrLevel.C2);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CEFR estimation failed; defaulting to Unknown.");
            return CefrLevel.Unknown;
        }
    }

    private static int Percentile(IReadOnlyList<int> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    private static IEnumerable<List<string>> Chunk(IEnumerable<string> source, int size)
    {
        var bucket = new List<string>(size);
        foreach (var item in source)
        {
            bucket.Add(item);
            if (bucket.Count == size)
            {
                yield return bucket;
                bucket = new List<string>(size);
            }
        }
        if (bucket.Count > 0) yield return bucket;
    }

    private static string NormalizeNewlines(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n');
}
