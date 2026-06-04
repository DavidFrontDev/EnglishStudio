using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

var jsonPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "external", "irregular_verbs.json"));
if (!File.Exists(jsonPath))
{
    Console.Error.WriteLine($"Not found: {jsonPath}");
    return 1;
}

var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
if (!doc.RootElement.TryGetProperty("verbs", out var verbsEl))
{
    Console.Error.WriteLine("Missing 'verbs' key.");
    return 1;
}

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;
await using var db = new DictionaryDbContext(options);

var verbsByLemma = await db.Words
    .Where(w => w.PartOfSpeech.Code == "v")
    .Select(w => new { w.Id, w.Lemma })
    .ToDictionaryAsync(x => x.Lemma, x => x.Id, StringComparer.OrdinalIgnoreCase);

var existingForms = await db.WordForms
    .Select(f => new { f.WordId, f.Form, f.Kind })
    .ToListAsync();
var existingSet = new HashSet<(int, string, WordFormKind)>(
    existingForms.Select(f => (f.WordId, f.Form, f.Kind)));

var inserted = 0;
var missed = new List<string>();
var verbCount = 0;
foreach (var v in verbsEl.EnumerateArray())
{
    verbCount++;
    var baseForm = v.GetProperty("base").GetString()?.ToLowerInvariant().Trim();
    var past = v.GetProperty("past").GetString()?.Trim();
    var pp = v.GetProperty("pastParticiple").GetString()?.Trim();
    if (string.IsNullOrWhiteSpace(baseForm)) continue;

    if (!verbsByLemma.TryGetValue(baseForm, out var wordId))
    {
        missed.Add(baseForm);
        continue;
    }

    void AddForm(string? form, WordFormKind kind)
    {
        if (string.IsNullOrWhiteSpace(form)) return;
        // Split "was/were" or "learnt/learned"
        var parts = form.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var f in parts)
        {
            if (existingSet.Add((wordId, f, kind)))
            {
                db.WordForms.Add(new WordForm
                {
                    WordId = wordId,
                    Form = f,
                    Kind = kind,
                    IsIrregular = true,
                });
                inserted++;
            }
        }
    }

    AddForm(past, WordFormKind.PastSimple);
    AddForm(pp, WordFormKind.PastParticiple);
}
await db.SaveChangesAsync();

Console.WriteLine($"Verbs in JSON      : {verbCount}");
Console.WriteLine($"Forms inserted     : {inserted}");
Console.WriteLine($"Missed (not in DB) : {missed.Count}");
if (missed.Count > 0)
{
    Console.WriteLine("First 15 missed verbs:");
    foreach (var m in missed.Take(15)) Console.WriteLine($"  {m}");
}
return 0;
