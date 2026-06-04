using System.IO;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// ReadingTokenizer lives in the module root namespace.
using EnglishStudio.Modules.Reading;

namespace EnglishStudio.Modules.Reading.Seed;

/// <summary>
/// Idempotently seeds the built-in graded readers (original texts shipped as an embedded JSON) into
/// <c>ReadingText</c> with <see cref="ReadingSource.Builtin"/>. Idempotency is by
/// (Title, Source=Builtin) so re-runs never duplicate. Triggered lazily from
/// <c>TextLibraryService.ListAsync</c> — no change to App startup.
/// </summary>
public sealed class GradedReaderSeedService
{
    private const string ResourceSuffix = "Seed.graded_readers.json";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly ILogger<GradedReaderSeedService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _seeded;

    public GradedReaderSeedService(IDbContextFactory<ReadingDbContext> factory, ILogger<GradedReaderSeedService> log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>Seeds once per process (idempotent in the DB regardless). Returns rows added this call.</summary>
    public async Task<int> EnsureSeededAsync(CancellationToken ct = default)
    {
        if (_seeded) return 0;
        await _gate.WaitAsync(ct);
        try
        {
            if (_seeded) return 0;
            var added = await SeedAsync(ct);
            _seeded = true;
            return added;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<int> SeedAsync(CancellationToken ct)
    {
        var items = LoadSeed();
        if (items.Count == 0) return 0;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.ReadingTexts
            .Where(t => t.Source == ReadingSource.Builtin)
            .Select(t => t.Title)
            .ToListAsync(ct);
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title) || string.IsNullOrWhiteSpace(item.Body)) continue;
            if (have.Contains(item.Title.Trim())) continue;

            var body = NormalizeNewlines(item.Body).Trim();
            db.ReadingTexts.Add(new ReadingText
            {
                Title = item.Title.Trim(),
                BodyText = body,
                Source = ReadingSource.Builtin,
                WordCount = ReadingTokenizer.CountWords(body),
                EstimatedCefr = ParseCefr(item.Level),
                Tags = string.IsNullOrWhiteSpace(item.Theme) ? null : item.Theme!.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Seeded {Count} built-in graded readers.", added);
        }
        return added;
    }

    private List<GradedReader> LoadSeed()
    {
        try
        {
            var asm = typeof(GradedReaderSeedService).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
            if (name is null)
            {
                _log.LogWarning("Graded readers resource not found ('{Suffix}').", ResourceSuffix);
                return new List<GradedReader>();
            }

            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<List<GradedReader>>(json, JsonOpts) ?? new List<GradedReader>();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load graded readers seed.");
            return new List<GradedReader>();
        }
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static CefrLevel ParseCefr(string? level) =>
        Enum.TryParse<CefrLevel>(level?.Trim(), ignoreCase: true, out var lvl) ? lvl : CefrLevel.Unknown;

    private sealed class GradedReader
    {
        public string? Title { get; set; }
        public string? Level { get; set; }
        public string? Body { get; set; }
        public string? Theme { get; set; }
    }
}
