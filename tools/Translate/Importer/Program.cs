using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

// Usage: dotnet run -- <cefr-folder>
// e.g.   dotnet run -- A1
//        Imports all JSON files from tools/Translate/output/A1/*.json into SQLite.

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run -- <CEFR>   (e.g. A1, A2, B1, ...)");
    return 1;
}

var cefrFolder = args[0];
var toolDir = AppContext.BaseDirectory;
// Walk up to tools/Translate/
var translateRoot = Path.GetFullPath(Path.Combine(toolDir, "..", "..", "..", ".."));
var outputDir = Path.Combine(translateRoot, "output", cefrFolder);

if (!Directory.Exists(outputDir))
{
    Console.Error.WriteLine($"Output directory not found: {outputDir}");
    return 1;
}

var batchFiles = Directory.GetFiles(outputDir, "batch_*.json").OrderBy(f => f).ToArray();
if (batchFiles.Length == 0)
{
    Console.Error.WriteLine($"No batch_*.json files in {outputDir}");
    return 1;
}

Console.WriteLine($"Found {batchFiles.Length} batch files in {outputDir}");

DictionaryPaths.EnsureDirectoriesExist();
var dbOptions = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

await using var db = new DictionaryDbContext(dbOptions);

// Build lookup: (Headword, PosCode) -> WordId. Some words have multiple senses; pick all senses ordered by OrderIndex.
var wordIndex = await db.Words
    .Include(w => w.PartOfSpeech)
    .Select(w => new { w.Id, w.Headword, PosCode = w.PartOfSpeech.Code })
    .ToListAsync();

var idLookup = wordIndex
    .GroupBy(w => (Norm(w.Headword), w.PosCode))
    .ToDictionary(g => g.Key, g => g.Select(w => w.Id).ToList());

Console.WriteLine($"Loaded {wordIndex.Count} words from DB; built lookup for {idLookup.Count} (headword,pos) keys.");

var totalMatched = 0;
var totalSensesUpdated = 0;
var totalTranslationsInserted = 0;
var totalUnmatched = 0;
var unmatched = new List<string>();

foreach (var batchFile in batchFiles)
{
    var json = await File.ReadAllTextAsync(batchFile);
    BatchOutput? batch;
    try
    {
        batch = JsonSerializer.Deserialize<BatchOutput>(json, jsonOptions);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  ✗ {Path.GetFileName(batchFile)}: JSON parse error — {ex.Message}");
        continue;
    }

    if (batch is null || batch.Translations is null)
    {
        Console.Error.WriteLine($"  ✗ {Path.GetFileName(batchFile)}: empty/null batch");
        continue;
    }

    var matchedInBatch = 0;
    foreach (var entry in batch.Translations)
    {
        var key = (Norm(entry.Headword), entry.Pos);
        if (!idLookup.TryGetValue(key, out var wordIds))
        {
            totalUnmatched++;
            unmatched.Add($"{entry.Headword} [{entry.Pos}]");
            continue;
        }

        foreach (var wordId in wordIds)
        {
            var senses = await db.Senses
                .Where(s => s.WordId == wordId)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();

            if (senses.Count == 0) continue;

            // Set DefinitionRu on the first sense (others remain empty until per-sense translations exist)
            var firstSense = senses[0];
            if (!string.IsNullOrWhiteSpace(entry.DefinitionRu))
            {
                firstSense.DefinitionRu = entry.DefinitionRu.Trim();
                totalSensesUpdated++;
            }

            // Insert Translation rows for the first sense. Replace existing if any (idempotency).
            var existing = await db.Translations.Where(t => t.SenseId == firstSense.Id).ToListAsync();
            if (existing.Count > 0)
            {
                db.Translations.RemoveRange(existing);
            }

            if (entry.Translations is { Count: > 0 })
            {
                for (var i = 0; i < entry.Translations.Count; i++)
                {
                    var text = entry.Translations[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    db.Translations.Add(new Translation
                    {
                        SenseId = firstSense.Id,
                        TextRu = text,
                        OrderIndex = i,
                    });
                    totalTranslationsInserted++;
                }
            }
        }

        matchedInBatch++;
        totalMatched++;
    }

    await db.SaveChangesAsync();
    Console.WriteLine($"  ✓ {Path.GetFileName(batchFile)}: matched {matchedInBatch}/{batch.Translations.Count}");
}

Console.WriteLine();
Console.WriteLine("─── SUMMARY ───");
Console.WriteLine($"  Matched entries        : {totalMatched}");
Console.WriteLine($"  Unmatched entries      : {totalUnmatched}");
Console.WriteLine($"  Senses updated         : {totalSensesUpdated}");
Console.WriteLine($"  Translation rows added : {totalTranslationsInserted}");

if (unmatched.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"Unmatched (first 20): {string.Join(", ", unmatched.Take(20))}");
}

return 0;

static string Norm(string s) => s.Trim().ToLowerInvariant();

public sealed class BatchOutput
{
    [JsonPropertyName("batchId")] public string BatchId { get; set; } = "";
    [JsonPropertyName("translations")] public List<EntryOutput> Translations { get; set; } = new();
}

public sealed class EntryOutput
{
    [JsonPropertyName("headword")] public string Headword { get; set; } = "";
    [JsonPropertyName("pos")] public string Pos { get; set; } = "";
    [JsonPropertyName("translations")] public List<string> Translations { get; set; } = new();
    [JsonPropertyName("definitionRu")] public string DefinitionRu { get; set; } = "";
}
