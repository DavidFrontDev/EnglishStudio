using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

var jsonPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "external", "ielts_topics.json"));
if (!File.Exists(jsonPath))
{
    Console.Error.WriteLine($"Not found: {jsonPath}");
    return 1;
}

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;
await using var db = new DictionaryDbContext(options);

var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
if (!doc.RootElement.TryGetProperty("categories", out var catsEl))
{
    Console.Error.WriteLine("Missing 'categories' key in JSON.");
    return 1;
}

// build word lookup: Lemma → list of WordId, preferring Source=Seed (Oxford 5000)
var allWords = await db.Words
    .Select(w => new { w.Id, w.Lemma, w.Source })
    .ToListAsync();
var wordByLemma = allWords
    .GroupBy(w => w.Lemma, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(
        g => g.Key,
        g => g.OrderBy(w => w.Source == WordSource.Seed ? 0 : 1).Select(w => w.Id).ToList(),
        StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"Loaded {allWords.Count} words; lemma keys: {wordByLemma.Count}");

var allCats = await db.Categories
    .Where(c => c.Code.StartsWith("ielts-topic-"))
    .ToDictionaryAsync(c => c.Code, c => c.Id);

if (allCats.Count == 0)
{
    Console.Error.WriteLine("No 'ielts-topic-*' categories in DB. Run the app first to seed them.");
    return 1;
}

var existingPairs = await db.WordCategories
    .Where(wc => allCats.Values.Contains(wc.CategoryId))
    .Select(wc => new { wc.WordId, wc.CategoryId })
    .ToListAsync();
var existingSet = new HashSet<(int, int)>(existingPairs.Select(p => (p.WordId, p.CategoryId)));

var totalLinked = 0;
var totalMissed = 0;
var missedSamples = new List<string>();

foreach (var topicProp in catsEl.EnumerateObject())
{
    var topicCode = topicProp.Name;
    var fullCode = $"ielts-topic-{topicCode}";
    if (!allCats.TryGetValue(fullCode, out var catId))
    {
        Console.WriteLine($"  (skip) no category for {fullCode}");
        continue;
    }
    if (topicProp.Value.ValueKind != JsonValueKind.Array) continue;

    var linkedHere = 0;
    var missedHere = 0;
    foreach (var w in topicProp.Value.EnumerateArray())
    {
        if (w.ValueKind != JsonValueKind.String) continue;
        var lemma = (w.GetString() ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lemma)) continue;

        if (!wordByLemma.TryGetValue(lemma, out var ids))
        {
            missedHere++;
            missedSamples.Add($"{topicCode}:{lemma}");
            continue;
        }
        foreach (var wid in ids)
        {
            if (!existingSet.Add((wid, catId))) continue;
            db.WordCategories.Add(new WordCategory { WordId = wid, CategoryId = catId });
            linkedHere++;
        }
    }
    Console.WriteLine($"  {topicCode,-12} linked={linkedHere,3}  missed={missedHere,3}");
    totalLinked += linkedHere;
    totalMissed += missedHere;
}

await db.SaveChangesAsync();
Console.WriteLine();
Console.WriteLine($"Total linked : {totalLinked}");
Console.WriteLine($"Total missed : {totalMissed}");
if (missedSamples.Count > 0)
{
    Console.WriteLine("First 20 missed:");
    foreach (var m in missedSamples.Take(20)) Console.WriteLine($"  {m}");
}
return 0;
