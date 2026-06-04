using System.Text.Json;
using System.Text.Json.Serialization;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

var toolDir = AppContext.BaseDirectory;
var translateRoot = Path.GetFullPath(Path.Combine(toolDir, "..", "..", "..", ".."));
var outputDir = Path.Combine(translateRoot, "output", "stubs");

if (!Directory.Exists(outputDir))
{
    Console.Error.WriteLine($"Output dir not found: {outputDir}");
    return 1;
}

var batchFiles = Directory.GetFiles(outputDir, "batch_stubs_*.json").OrderBy(f => f).ToArray();
if (batchFiles.Length == 0)
{
    Console.Error.WriteLine($"No batch_stubs_*.json in {outputDir}");
    return 1;
}

Console.WriteLine($"Found {batchFiles.Length} batch files.");

DictionaryPaths.EnsureDirectoriesExist();
var dbOptions = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

await using var db = new DictionaryDbContext(dbOptions);

var totalItems = 0;
var sensesUpdated = 0;
var translationsInserted = 0;
var notFoundIds = new List<string>();

foreach (var file in batchFiles)
{
    var text = await File.ReadAllTextAsync(file);
    StubBatch? batch;
    try
    {
        batch = JsonSerializer.Deserialize<StubBatch>(text, jsonOptions);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  ✗ {Path.GetFileName(file)}: parse error — {ex.Message}");
        continue;
    }
    if (batch?.Items is null) continue;

    foreach (var item in batch.Items)
    {
        totalItems++;
        if (item.Kind == "word")
        {
            var senses = await db.Senses
                .Where(s => s.WordId == item.Id)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
            if (senses.Count == 0)
            {
                notFoundIds.Add($"word:{item.Id}:{item.Headword}");
                continue;
            }
            ApplyTranslations(db, senses, item.Senses, ref sensesUpdated, ref translationsInserted);
        }
        else if (item.Kind == "phrasal")
        {
            var senses = await db.Senses
                .Where(s => s.PhrasalVerbId == item.Id)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
            if (senses.Count == 0)
            {
                notFoundIds.Add($"phrasal:{item.Id}:{item.Headword}");
                continue;
            }
            ApplyTranslations(db, senses, item.Senses, ref sensesUpdated, ref translationsInserted);
        }
    }

    await db.SaveChangesAsync();
    Console.WriteLine($"  ✓ {Path.GetFileName(file)} processed.");
}

Console.WriteLine();
Console.WriteLine($"Items processed   : {totalItems}");
Console.WriteLine($"Senses updated    : {sensesUpdated}");
Console.WriteLine($"Translations new  : {translationsInserted}");
Console.WriteLine($"Not found in DB   : {notFoundIds.Count}");
if (notFoundIds.Count > 0)
{
    Console.WriteLine("First missing:");
    foreach (var x in notFoundIds.Take(10)) Console.WriteLine($"  - {x}");
}

return 0;

static void ApplyTranslations(
    DictionaryDbContext db,
    List<Sense> senses,
    List<StubSenseOut>? incoming,
    ref int sensesUpdated,
    ref int translationsInserted)
{
    if (incoming is null) return;
    var byIndex = incoming.ToDictionary(s => s.Index);
    var orderIdx = 0;
    foreach (var sense in senses)
    {
        if (!byIndex.TryGetValue(orderIdx, out var inc))
        {
            orderIdx++;
            continue;
        }
        orderIdx++;

        if (!string.IsNullOrWhiteSpace(inc.DefRu))
        {
            sense.DefinitionRu = inc.DefRu.Trim();
            sensesUpdated++;
        }
        if (inc.TranslationsRu is { Count: > 0 })
        {
            var existing = db.Translations.Where(t => t.SenseId == sense.Id).ToList();
            db.Translations.RemoveRange(existing);
            var tIdx = 0;
            foreach (var tr in inc.TranslationsRu.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                db.Translations.Add(new Translation
                {
                    SenseId = sense.Id,
                    TextRu = tr.Trim(),
                    OrderIndex = tIdx++,
                });
                translationsInserted++;
            }
        }
    }
}

internal sealed class StubBatch
{
    [JsonPropertyName("BatchId")] public string? BatchId { get; set; }
    [JsonPropertyName("Count")] public int Count { get; set; }
    [JsonPropertyName("Items")] public List<StubItem>? Items { get; set; }
}

internal sealed class StubItem
{
    [JsonPropertyName("Kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Headword")] public string Headword { get; set; } = string.Empty;
    [JsonPropertyName("Pos")] public string Pos { get; set; } = string.Empty;
    [JsonPropertyName("Senses")] public List<StubSenseOut>? Senses { get; set; }
}

internal sealed class StubSenseOut
{
    [JsonPropertyName("Index")] public int Index { get; set; }
    [JsonPropertyName("DefEn")] public string DefEn { get; set; } = string.Empty;
    [JsonPropertyName("ExampleEn")] public string? ExampleEn { get; set; }
    [JsonPropertyName("DefRu")] public string? DefRu { get; set; }
    [JsonPropertyName("TranslationsRu")] public List<string>? TranslationsRu { get; set; }
}
