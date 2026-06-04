using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

var externalDir = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "external"));

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;
await using var db = new DictionaryDbContext(options);

await ImportLinkingPhrasesAsync(db, Path.Combine(externalDir, "ielts_linking_phrases.json"));
await ImportTask1VocabAsync(db, Path.Combine(externalDir, "ielts_task1_vocab.json"));
await ImportCollocationsAsync(db, Path.Combine(externalDir, "ielts_collocations.json"));

return 0;

static async Task<int> GetOrCreatePosIdAsync(DictionaryDbContext db, string code, string nameEn, string nameRu)
{
    var existing = await db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Code == code);
    if (existing is not null) return existing.Id;
    var pos = new PartOfSpeech { Code = code, NameEn = nameEn, NameRu = nameRu };
    db.PartsOfSpeech.Add(pos);
    await db.SaveChangesAsync();
    return pos.Id;
}

static async Task<Tag> GetOrCreateTagAsync(DictionaryDbContext db, string code, string nameRu)
{
    var existing = await db.Tags.FirstOrDefaultAsync(t => t.Code == code);
    if (existing is not null) return existing;
    var t = new Tag { Code = code, NameRu = nameRu };
    db.Tags.Add(t);
    await db.SaveChangesAsync();
    return t;
}

static async Task ImportLinkingPhrasesAsync(DictionaryDbContext db, string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"Skip (not found): {path}"); return; }
    Console.WriteLine($"Importing linking phrases from {Path.GetFileName(path)}...");

    var phrasePosId = await GetOrCreatePosIdAsync(db, "phrase", "Phrase", "Фраза");
    var ieltsWritingTag = await GetOrCreateTagAsync(db, "ielts-writing-linking", "IELTS Writing: связка");

    var existingByLemma = await db.Words
        .Where(w => w.PartOfSpeechId == phrasePosId)
        .ToDictionaryAsync(w => w.Lemma, w => w, StringComparer.OrdinalIgnoreCase);

    var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    if (!doc.RootElement.TryGetProperty("phrases", out var arr)) return;

    var now = DateTime.UtcNow;
    var created = 0;
    var skipped = 0;
    var newTagLinks = new List<WordTag>();
    var allCategoryTags = new Dictionary<string, Tag>();

    foreach (var p in arr.EnumerateArray())
    {
        var headword = p.GetProperty("headword").GetString()?.Trim() ?? "";
        var category = p.GetProperty("category").GetString()?.Trim().ToLowerInvariant() ?? "";
        var defEn = p.GetProperty("definitionEn").GetString()?.Trim() ?? "";
        var defRu = p.GetProperty("definitionRu").GetString()?.Trim() ?? "";
        var translations = p.TryGetProperty("translationsRu", out var trEl) && trEl.ValueKind == JsonValueKind.Array
            ? trEl.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList()
            : new List<string>();
        var exampleEn = p.TryGetProperty("exampleEn", out var ex) ? ex.GetString() : null;
        var lemma = headword.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lemma)) continue;

        if (existingByLemma.ContainsKey(lemma)) { skipped++; continue; }

        var word = new Word
        {
            Headword = headword,
            Lemma = lemma,
            PartOfSpeechId = phrasePosId,
            Source = WordSource.Seed,
            CefrLevel = CefrLevel.Unknown,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sense = new Sense
        {
            DefinitionEn = defEn,
            DefinitionRu = defRu,
            OrderIndex = 0,
        };
        var tIdx = 0;
        foreach (var t in translations)
        {
            sense.Translations.Add(new Translation { TextRu = t, OrderIndex = tIdx++ });
        }
        if (!string.IsNullOrWhiteSpace(exampleEn))
        {
            var exObj = new Example { TextEn = exampleEn!, Source = "ielts-linking" };
            sense.Examples.Add(exObj);
            word.Examples.Add(exObj);
        }
        word.Senses.Add(sense);
        db.Words.Add(word);
        existingByLemma[lemma] = word;
        created++;

        Tag catTag;
        if (!allCategoryTags.TryGetValue(category, out catTag!))
        {
            catTag = await GetOrCreateTagAsync(db, $"ielts-writing-{category}", $"IELTS Writing: {category}");
            allCategoryTags[category] = catTag;
        }
        // tag will be saved after Words.SaveChanges
        word.WordTags.Add(new WordTag { Tag = ieltsWritingTag });
        word.WordTags.Add(new WordTag { Tag = catTag });
    }
    await db.SaveChangesAsync();
    Console.WriteLine($"  Linking phrases created: {created}, skipped (already exist): {skipped}");
}

static async Task ImportTask1VocabAsync(DictionaryDbContext db, string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"Skip (not found): {path}"); return; }
    Console.WriteLine($"Importing Task 1 vocab from {Path.GetFileName(path)}...");

    var phrasePosId = await GetOrCreatePosIdAsync(db, "phrase", "Phrase", "Фраза");
    var posByCode = await db.PartsOfSpeech.ToDictionaryAsync(p => p.Code, p => p.Id);
    var ieltsTask1Tag = await GetOrCreateTagAsync(db, "ielts-writing-task1", "IELTS Writing Task 1");

    var existingByKey = await db.Words
        .Select(w => new { w.Id, w.Lemma, w.PartOfSpeechId })
        .ToListAsync();
    var existingByLemmaPos = existingByKey
        .GroupBy(x => (x.Lemma.ToLowerInvariant(), x.PartOfSpeechId))
        .ToDictionary(g => g.Key, g => g.First().Id);

    var existingTagsForWords = await db.WordTags
        .Select(wt => new { wt.WordId, wt.TagId })
        .ToListAsync();
    var existingTagSet = new HashSet<(int, int)>(
        existingTagsForWords.Select(wt => (wt.WordId, wt.TagId)));

    var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    if (!doc.RootElement.TryGetProperty("items", out var arr)) return;

    var now = DateTime.UtcNow;
    var created = 0;
    var taggedExisting = 0;
    var categoryTags = new Dictionary<string, Tag>();

    foreach (var p in arr.EnumerateArray())
    {
        var headword = p.GetProperty("headword").GetString()?.Trim() ?? "";
        var posCode = p.GetProperty("pos").GetString()?.Trim() ?? "phrase";
        var category = p.GetProperty("category").GetString()?.Trim().ToLowerInvariant() ?? "";
        var defEn = p.GetProperty("definitionEn").GetString()?.Trim() ?? "";
        var defRu = p.GetProperty("definitionRu").GetString()?.Trim() ?? "";
        var translations = p.TryGetProperty("translationsRu", out var trEl) && trEl.ValueKind == JsonValueKind.Array
            ? trEl.EnumerateArray().Select(x => x.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>().ToList()
            : new List<string>();
        var exampleEn = p.TryGetProperty("exampleEn", out var ex) ? ex.GetString() : null;
        var lemma = headword.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lemma)) continue;

        var posId = posByCode.TryGetValue(posCode, out var pid) ? pid : phrasePosId;
        var key = (lemma, posId);

        Tag catTag;
        if (!categoryTags.TryGetValue(category, out catTag!))
        {
            catTag = await GetOrCreateTagAsync(db, $"ielts-task1-{category}", $"IELTS Task 1: {category}");
            categoryTags[category] = catTag;
        }

        if (existingByLemmaPos.TryGetValue(key, out var existingId))
        {
            if (existingTagSet.Add((existingId, ieltsTask1Tag.Id)))
                db.WordTags.Add(new WordTag { WordId = existingId, TagId = ieltsTask1Tag.Id });
            if (existingTagSet.Add((existingId, catTag.Id)))
                db.WordTags.Add(new WordTag { WordId = existingId, TagId = catTag.Id });
            taggedExisting++;
            continue;
        }

        var word = new Word
        {
            Headword = headword,
            Lemma = lemma,
            PartOfSpeechId = posId,
            Source = WordSource.Seed,
            CefrLevel = CefrLevel.Unknown,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var sense = new Sense
        {
            DefinitionEn = defEn,
            DefinitionRu = defRu,
            OrderIndex = 0,
        };
        var tIdx = 0;
        foreach (var t in translations)
        {
            sense.Translations.Add(new Translation { TextRu = t, OrderIndex = tIdx++ });
        }
        if (!string.IsNullOrWhiteSpace(exampleEn))
        {
            var exObj = new Example { TextEn = exampleEn!, Source = "ielts-task1" };
            sense.Examples.Add(exObj);
            word.Examples.Add(exObj);
        }
        word.Senses.Add(sense);
        word.WordTags.Add(new WordTag { Tag = ieltsTask1Tag });
        word.WordTags.Add(new WordTag { Tag = catTag });
        db.Words.Add(word);
        existingByLemmaPos[key] = -1; // sentinel; real id post-save
        created++;
    }

    try { await db.SaveChangesAsync(); }
    catch (DbUpdateException dbex)
    {
        Console.Error.WriteLine($"  Save error: {dbex.InnerException?.Message ?? dbex.Message}");
    }
    Console.WriteLine($"  Task 1 vocab created: {created}, tagged existing: {taggedExisting}");
}

static async Task ImportCollocationsAsync(DictionaryDbContext db, string path)
{
    if (!File.Exists(path)) { Console.WriteLine($"Skip (not found): {path}"); return; }
    Console.WriteLine($"Importing collocations from {Path.GetFileName(path)}...");

    var wordList = await db.Words.Select(w => new { w.Id, w.Lemma }).ToListAsync();
    var wordByLemma = wordList
        .GroupBy(x => x.Lemma, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

    var existing = await db.Collocations
        .Select(c => new { c.LinkedText, c.Pattern })
        .ToListAsync();
    var existingSet = new HashSet<(string, CollocationPattern)>(
        existing.Select(c => (c.LinkedText.ToLowerInvariant(), c.Pattern)));

    var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    if (!doc.RootElement.TryGetProperty("collocations", out var arr)) return;

    var now = DateTime.UtcNow;
    var inserted = 0;
    var skipped = 0;

    foreach (var p in arr.EnumerateArray())
    {
        var headword = p.GetProperty("headword").GetString()?.Trim() ?? "";
        var linkedText = p.GetProperty("linkedText").GetString()?.Trim() ?? "";
        var patternStr = p.GetProperty("pattern").GetString()?.Trim() ?? "";
        var defEn = p.GetProperty("definitionEn").GetString()?.Trim() ?? "";
        var transRu = p.GetProperty("translationRu").GetString()?.Trim() ?? "";
        var exampleEn = p.TryGetProperty("exampleEn", out var ex) ? ex.GetString() : null;

        var pattern = patternStr switch
        {
            "v+n"        => CollocationPattern.VerbNoun,
            "adj+n"      => CollocationPattern.AdjectiveNoun,
            "v+adv"      => CollocationPattern.VerbAdverb,
            "adv+adj"    => CollocationPattern.AdverbAdjective,
            "n+n"        => CollocationPattern.NounNoun,
            "n+prep+n"   => CollocationPattern.NounPrepNoun,
            _            => CollocationPattern.Unknown,
        };

        var key = (linkedText.ToLowerInvariant(), pattern);
        if (existingSet.Contains(key)) { skipped++; continue; }
        existingSet.Add(key);

        int? headWordId = wordByLemma.TryGetValue(headword.ToLowerInvariant(), out var hid) ? hid : null;

        db.Collocations.Add(new Collocation
        {
            HeadWordId = headWordId,
            Headword = headword,
            LinkedText = linkedText,
            Pattern = pattern,
            DefinitionEn = defEn,
            TranslationRu = transRu,
            ExampleEn = exampleEn,
            Source = WordSource.Seed,
            CreatedAt = now,
        });
        inserted++;
    }
    await db.SaveChangesAsync();
    Console.WriteLine($"  Collocations inserted: {inserted}, skipped (dup): {skipped}");
}
