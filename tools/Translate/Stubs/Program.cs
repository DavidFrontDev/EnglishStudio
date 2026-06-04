using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

const int batchSize = 110;

DictionaryPaths.EnsureDirectoriesExist();
var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;
await using var db = new DictionaryDbContext(options);

var words = await db.Words
    .Include(w => w.PartOfSpeech)
    .Include(w => w.Senses).ThenInclude(s => s.Examples)
    .Where(w => (w.Source == WordSource.Awl || w.Source == WordSource.Avl) && w.Senses.Any()
                && !w.Senses.Any(s => s.DefinitionRu != ""))
    .OrderBy(w => w.Headword)
    .ToListAsync();

var phrasals = await db.PhrasalVerbs
    .Include(p => p.Senses).ThenInclude(s => s.Examples)
    .Where(p => !p.Senses.Any(s => s.DefinitionRu != ""))
    .OrderBy(p => p.Headword)
    .ToListAsync();

Console.WriteLine($"Words to translate    : {words.Count}");
Console.WriteLine($"Phrasals to translate : {phrasals.Count}");

var items = new List<TranslateItem>();
foreach (var w in words)
{
    items.Add(new TranslateItem(
        Kind: "word",
        Id: w.Id,
        Headword: w.Headword,
        Pos: w.PartOfSpeech.Code,
        Senses: w.Senses.OrderBy(s => s.OrderIndex).Select((s, idx) => new TranslateSense(
            Index: idx,
            DefEn: s.DefinitionEn,
            ExampleEn: s.Examples.FirstOrDefault()?.TextEn
        )).ToList()));
}
foreach (var p in phrasals)
{
    items.Add(new TranslateItem(
        Kind: "phrasal",
        Id: p.Id,
        Headword: p.Headword,
        Pos: "phrasal_verb",
        Senses: p.Senses.OrderBy(s => s.OrderIndex).Select((s, idx) => new TranslateSense(
            Index: idx,
            DefEn: s.DefinitionEn,
            ExampleEn: s.Examples.FirstOrDefault()?.TextEn
        )).ToList()));
}

var inputDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "input", "stubs"));
if (Directory.Exists(inputDir)) Directory.Delete(inputDir, recursive: true);
Directory.CreateDirectory(inputDir);

var batches = (int)Math.Ceiling(items.Count / (double)batchSize);
for (var i = 0; i < batches; i++)
{
    var slice = items.Skip(i * batchSize).Take(batchSize).ToList();
    var doc = new TranslateBatch(
        BatchId: $"stubs_{i + 1:D3}",
        Count: slice.Count,
        Items: slice);
    var path = Path.Combine(inputDir, $"batch_stubs_{i + 1:D3}.json");
    await using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, doc, new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
    Console.WriteLine($"  → {path} ({slice.Count} items)");
}

Console.WriteLine($"\nTotal items: {items.Count} across {batches} batch(es).");

internal record TranslateSense(int Index, string DefEn, string? ExampleEn);
internal record TranslateItem(string Kind, int Id, string Headword, string Pos, List<TranslateSense> Senses);
internal record TranslateBatch(string BatchId, int Count, List<TranslateItem> Items);
