using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;

await using var db = new DictionaryDbContext(options);

Console.WriteLine($"DB: {DictionaryPaths.DatabaseFilePath}");
Console.WriteLine();

Console.WriteLine($"  PartsOfSpeech : {await db.PartsOfSpeech.CountAsync(),6}");
Console.WriteLine($"  Words         : {await db.Words.CountAsync(),6}");
Console.WriteLine($"  Senses        : {await db.Senses.CountAsync(),6}");
Console.WriteLine($"  Examples      : {await db.Examples.CountAsync(),6}");
Console.WriteLine($"  Categories    : {await db.Categories.CountAsync(),6}");
Console.WriteLine($"  Translations  : {await db.Translations.CountAsync(),6}");
Console.WriteLine();

Console.WriteLine("By CEFR level:");
var cefrStats = await db.Words
    .GroupBy(w => w.CefrLevel)
    .Select(g => new { Level = g.Key, Count = g.Count() })
    .OrderBy(x => x.Level)
    .ToListAsync();
foreach (var s in cefrStats)
{
    Console.WriteLine($"  {s.Level,-8} {s.Count,5}");
}
Console.WriteLine();

Console.WriteLine("By Part of Speech (top 10):");
var posStats = await db.PartsOfSpeech
    .Select(p => new { p.Code, p.NameRu, Count = p.Words.Count })
    .OrderByDescending(x => x.Count)
    .Take(10)
    .ToListAsync();
foreach (var s in posStats)
{
    Console.WriteLine($"  {s.Code,-10} {s.NameRu,-30} {s.Count,5}");
}
Console.WriteLine();

Console.WriteLine("By Source:");
var sourceStats = await db.Words
    .GroupBy(w => w.Source)
    .Select(g => new { Source = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .ToListAsync();
foreach (var s in sourceStats)
{
    Console.WriteLine($"  {s.Source,-10} {s.Count,6}");
}
Console.WriteLine();

Console.WriteLine("Tag totals (top 15):");
var tagStats = await db.Tags
    .Select(t => new { t.Code, t.NameRu, Count = t.WordTags.Count })
    .OrderByDescending(x => x.Count)
    .Take(15)
    .ToListAsync();
foreach (var t in tagStats)
{
    Console.WriteLine($"  {t.Code,-20} {t.NameRu,-30} {t.Count,6}");
}
Console.WriteLine();

Console.WriteLine("PhrasalVerbs:");
Console.WriteLine($"  Count : {await db.PhrasalVerbs.CountAsync(),6}");
Console.WriteLine();

Console.WriteLine("Collocations by pattern:");
var collGroups = await db.Collocations
    .GroupBy(c => c.Pattern)
    .Select(g => new { Pattern = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .ToListAsync();
foreach (var g in collGroups)
{
    Console.WriteLine($"  {g.Pattern,-20} {g.Count,6}");
}
Console.WriteLine($"  Total                : {await db.Collocations.CountAsync(),6}");
Console.WriteLine();

Console.WriteLine("IELTS Categories with words:");
var catStats = await db.Categories
    .Where(c => c.Code.StartsWith("ielts-topic-"))
    .Select(c => new { c.Code, c.NameRu, Count = c.WordCategories.Count })
    .OrderBy(c => c.Code)
    .ToListAsync();
foreach (var s in catStats)
{
    Console.WriteLine($"  {s.Code,-25} {s.NameRu,-30} {s.Count,4}");
}
Console.WriteLine();

Console.WriteLine("WordForms (irregular):");
Console.WriteLine($"  Total irregular forms: {await db.WordForms.CountAsync(f => f.IsIrregular),6}");
Console.WriteLine();

Console.WriteLine("Examples by source:");
var exSourceStats = await db.Examples
    .GroupBy(e => e.Source ?? "(null)")
    .Select(g => new { Source = g.Key, Count = g.Count() })
    .OrderByDescending(x => x.Count)
    .ToListAsync();
foreach (var s in exSourceStats)
{
    Console.WriteLine($"  {s.Source,-25} {s.Count,6}");
}
Console.WriteLine();

Console.WriteLine("MediaAssets:");
Console.WriteLine($"  Audio : {await db.MediaAssets.CountAsync(m => m.Kind == EnglishStudio.Modules.Dictionary.Entities.MediaKind.Audio),6}");
Console.WriteLine($"  Image : {await db.MediaAssets.CountAsync(m => m.Kind == EnglishStudio.Modules.Dictionary.Entities.MediaKind.Image),6}");
Console.WriteLine();

Console.WriteLine("SRS / Training:");
Console.WriteLine($"  UserWordProgress total : {await db.UserWordProgress.CountAsync(),6}");
Console.WriteLine($"  ReviewLogs total       : {await db.ReviewLogs.CountAsync(),6}");
Console.WriteLine();

Console.WriteLine("Pronunciation:");
var pronCount = await db.PronunciationAttempts.CountAsync();
var pronAvg = pronCount == 0 ? 0.0 : await db.PronunciationAttempts.AverageAsync(p => (double)p.Score);
Console.WriteLine($"  Attempts total : {pronCount,6}");
Console.WriteLine($"  Avg score      : {pronAvg,6:F1}");
Console.WriteLine();

Console.WriteLine("Audio paths populated:");
var audioUk = await db.Words.CountAsync(w => w.AudioUkPath != null);
var audioUs = await db.Words.CountAsync(w => w.AudioUsPath != null);
var bothNull = await db.Words.CountAsync(w => w.AudioUkPath == null && w.AudioUsPath == null);
Console.WriteLine($"  AudioUkPath   : {audioUk,6}");
Console.WriteLine($"  AudioUsPath   : {audioUs,6}");
Console.WriteLine($"  Both null     : {bothNull,6}");
Console.WriteLine();

Console.WriteLine("Sample A1 words with Russian translations (first 8):");
Console.OutputEncoding = System.Text.Encoding.UTF8;
var sample = await db.Words
    .Where(w => w.CefrLevel == CefrLevel.A1)
    .OrderBy(w => w.Headword)
    .Take(8)
    .Select(w => new
    {
        w.Headword,
        Pos = w.PartOfSpeech.Code,
        FirstSense = w.Senses
            .OrderBy(s => s.OrderIndex)
            .Select(s => new
            {
                s.DefinitionRu,
                Translations = s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu).ToList()
            })
            .FirstOrDefault(),
    })
    .ToListAsync();
foreach (var w in sample)
{
    var trs = string.Join(", ", w.FirstSense?.Translations ?? new List<string>());
    var def = w.FirstSense?.DefinitionRu ?? "(empty)";
    var defSnippet = def.Length > 60 ? def.Substring(0, 60) + "…" : def;
    Console.WriteLine($"  {w.Headword,-12} [{w.Pos,-4}] {trs}");
    Console.WriteLine($"               → {defSnippet}");
}
