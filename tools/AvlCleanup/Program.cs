using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;

await using var db = new DictionaryDbContext(options);

Console.WriteLine("Перед чисткой:");
Console.WriteLine($"  Words.Source=Avl              : {await db.Words.CountAsync(w => w.Source == WordSource.Avl),6}");
Console.WriteLine($"  Words.Source=Avl + rank>3000  : {await db.Words.CountAsync(w => w.Source == WordSource.Avl && (w.FrequencyRank == null || w.FrequencyRank > 3000)),6}");
Console.WriteLine();

Console.WriteLine("Удаляю AVL-теги (avl, avl-band-*) — каскад снесёт WordTag rows...");
var avlTags = await db.Tags
    .Where(t => t.Code == "avl" || t.Code.StartsWith("avl-band-"))
    .ToListAsync();
db.Tags.RemoveRange(avlTags);
await db.SaveChangesAsync();
Console.WriteLine($"  Удалено тегов: {avlTags.Count}");

Console.WriteLine("Удаляю AVL stubs где FrequencyRank > 3000 или null...");
var stubsToDelete = await db.Words
    .Where(w => w.Source == WordSource.Avl && (w.FrequencyRank == null || w.FrequencyRank > 3000))
    .ToListAsync();
db.Words.RemoveRange(stubsToDelete);
await db.SaveChangesAsync();
Console.WriteLine($"  Удалено stub-слов: {stubsToDelete.Count}");

Console.WriteLine();
Console.WriteLine("После чистки:");
Console.WriteLine($"  Words.Source=Avl : {await db.Words.CountAsync(w => w.Source == WordSource.Avl),6}");
Console.WriteLine($"  AVL Tag count    : {await db.Tags.CountAsync(t => t.Code == "avl" || t.Code.StartsWith("avl-band-")),6}");
Console.WriteLine($"  Total Words      : {await db.Words.CountAsync(),6}");
Console.WriteLine();
Console.WriteLine("Готово. При следующем запуске EnglishStudio.App SeedAvlIfEmptyAsync пересидит AVL с фильтром rank ≤ 3000.");
