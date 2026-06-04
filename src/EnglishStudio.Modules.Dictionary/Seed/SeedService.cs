using System.Diagnostics;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Seed;

public class SeedService
{
    private const string DictionaryContentFolder = "Dictionary";

    private readonly DictionaryDbContext _db;
    private readonly IContentStore _content;
    private readonly ILogger<SeedService> _logger;

    public SeedService(DictionaryDbContext db, IContentStore content, ILogger<SeedService> logger)
    {
        _db = db;
        _content = content;
        _logger = logger;
    }

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await _db.Words.AnyAsync(ct))
        {
            _logger.LogInformation("Dictionary already seeded ({Count} words); skipping.",
                await _db.Words.CountAsync(ct));
            return;
        }

        var sw = Stopwatch.StartNew();
        await SeedOxford5000Async(ct);
        sw.Stop();
        _logger.LogInformation("Seed completed in {Elapsed} ms.", sw.ElapsedMilliseconds);
    }

    private async Task SeedOxford5000Async(CancellationToken ct)
    {
        var stream = _content.OpenJson(DictionaryContentFolder, "oxford_5000.json");
        if (stream is null)
        {
            _logger.LogInformation("Dictionary (Oxford 5000): content not imported, skipping seed.");
            return;
        }

        _logger.LogInformation("Reading imported Oxford 5000 seed...");

        OxfordSeedDocument doc;
        await using (stream)
        {
            doc = await JsonSerializer.DeserializeAsync<OxfordSeedDocument>(stream,
                       cancellationToken: ct)
                  ?? throw new InvalidOperationException("Seed file is empty or malformed.");
        }

        _logger.LogInformation("Loaded {Count} entries; inserting into SQLite...",
            doc.Words.Count);

        var posByCode = await SeedPartsOfSpeechAsync(ct);

        var prevAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var now = DateTime.UtcNow;
            var inserted = 0;
            var grouped = doc.Words.GroupBy(w => (w.Headword, w.Pos));

            foreach (var group in grouped)
            {
                if (!posByCode.TryGetValue(group.Key.Pos, out var partOfSpeechId))
                {
                    partOfSpeechId = posByCode["other"];
                }

                var first = group.First();
                var word = new Word
                {
                    Headword = first.Headword.Trim(),
                    Lemma = first.Headword.Trim().ToLowerInvariant(),
                    IpaUk = NormalizeIpa(first.IpaUk),
                    IpaUs = NormalizeIpa(first.IpaUs),
                    AudioUkPath = NormalizeAudioPath(first.AudioUk),
                    AudioUsPath = NormalizeAudioPath(first.AudioUs),
                    CefrLevel = ParseCefr(first.Cefr),
                    Source = WordSource.Seed,
                    PartOfSpeechId = partOfSpeechId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var orderIdx = 0;
                foreach (var entry in group)
                {
                    var sense = new Sense
                    {
                        DefinitionEn = (entry.DefinitionEn ?? string.Empty).Trim(),
                        DefinitionRu = string.Empty,
                        OrderIndex = orderIdx,
                    };

                    if (!string.IsNullOrWhiteSpace(entry.ExampleEn))
                    {
                        var example = new Example
                        {
                            TextEn = entry.ExampleEn.Trim(),
                            Source = "oxford-5000",
                        };
                        sense.Examples.Add(example);
                        word.Examples.Add(example);
                    }

                    word.Senses.Add(sense);
                    orderIdx++;
                }

                _db.Words.Add(word);
                inserted++;

                if (inserted % 500 == 0)
                {
                    _db.ChangeTracker.DetectChanges();
                    await _db.SaveChangesAsync(ct);
                    _db.ChangeTracker.Clear();
                    _logger.LogDebug("Seeded {Count} words so far...", inserted);
                }
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();

            _logger.LogInformation("Inserted {Total} words.", inserted);
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = prevAutoDetect;
        }
    }

    private async Task<Dictionary<string, int>> SeedPartsOfSpeechAsync(CancellationToken ct)
    {
        var existing = await _db.PartsOfSpeech.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        if (existing.Count > 0) return existing;

        foreach (var (code, nameEn, nameRu) in PartOfSpeechSeedMap.All)
        {
            _db.PartsOfSpeech.Add(PartOfSpeechSeedMap.Create(code, nameEn, nameRu));
        }
        await _db.SaveChangesAsync(ct);

        return await _db.PartsOfSpeech.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
    }

    private static CefrLevel ParseCefr(string? raw) => raw?.ToUpperInvariant() switch
    {
        "A1" => CefrLevel.A1,
        "A2" => CefrLevel.A2,
        "B1" => CefrLevel.B1,
        "B2" => CefrLevel.B2,
        "C1" => CefrLevel.C1,
        "C2" => CefrLevel.C2,
        _    => CefrLevel.Unknown,
    };

    private static string? NormalizeIpa(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? NormalizeAudioPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public async Task SeedAwlIfEmptyAsync(CancellationToken ct = default)
    {
        // idempotency: если уже есть теги awl-sublist-* в БД — пропускаем
        if (await _db.Tags.AnyAsync(t => t.Code == "awl-sublist-1", ct))
        {
            _logger.LogInformation("AWL already seeded; skipping.");
            return;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Reading embedded AWL seed...");
        List<AwlEntry> entries;
        await using (var s = SeedManifest.OpenAwl())
            entries = AcademicSeedReader.ReadAwl(s);

        // Tag rows
        var awlTag = await GetOrCreateTagAsync("awl", "AWL", ct);
        var sublistTags = new Dictionary<int, Tag>();
        for (var i = 1; i <= 10; i++)
        {
            sublistTags[i] = await GetOrCreateTagAsync($"awl-sublist-{i}", $"AWL: список {i}", ct);
        }

        // ensure 'other' POS for stubs
        var otherPosId = await GetOrCreatePartOfSpeechIdAsync("other", "Other", "Прочее", ct);

        // lemma → wordIds index
        var allWords = await _db.Words
            .Select(w => new { w.Id, w.Headword, w.Lemma })
            .ToListAsync(ct);
        var byKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in allWords)
        {
            Add(byKey, w.Headword, w.Id);
            Add(byKey, w.Lemma, w.Id);
        }

        var existingPairs = await _db.WordTags
            .Where(wt => wt.Tag.Code.StartsWith("awl"))
            .Select(wt => new { wt.WordId, wt.TagId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(int, int)>(existingPairs.Select(p => (p.WordId, p.TagId)));

        var now = DateTime.UtcNow;
        var newWords = new List<Word>();
        var stubSublist = new List<int>(); // parallel to newWords: sublist number
        var newTags = new List<WordTag>();
        var taggedExisting = 0;
        var createdStubs = 0;

        foreach (var e in entries)
        {
            var matchedIds = new HashSet<int>();
            if (byKey.TryGetValue(e.Headword, out var ids)) foreach (var id in ids) matchedIds.Add(id);
            foreach (var fam in e.Family)
            {
                if (byKey.TryGetValue(fam, out var fids)) foreach (var id in fids) matchedIds.Add(id);
            }

            if (matchedIds.Count == 0)
            {
                var stub = new Word
                {
                    Headword = e.Headword,
                    Lemma = e.Headword.ToLowerInvariant(),
                    Source = WordSource.Awl,
                    CefrLevel = CefrLevel.Unknown,
                    PartOfSpeechId = otherPosId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                newWords.Add(stub);
                stubSublist.Add(e.Sublist);
                createdStubs++;
                continue;
            }

            foreach (var wordId in matchedIds)
            {
                if (existingSet.Add((wordId, awlTag.Id)))
                    newTags.Add(new WordTag { WordId = wordId, TagId = awlTag.Id });
                var subTag = sublistTags[e.Sublist];
                if (existingSet.Add((wordId, subTag.Id)))
                    newTags.Add(new WordTag { WordId = wordId, TagId = subTag.Id });
                taggedExisting++;
            }
        }

        if (newWords.Count > 0)
        {
            _db.Words.AddRange(newWords);
            await _db.SaveChangesAsync(ct);
            for (var i = 0; i < newWords.Count; i++)
            {
                newTags.Add(new WordTag { WordId = newWords[i].Id, TagId = awlTag.Id });
                newTags.Add(new WordTag { WordId = newWords[i].Id, TagId = sublistTags[stubSublist[i]].Id });
            }
        }

        if (newTags.Count > 0)
        {
            _db.WordTags.AddRange(newTags);
            await _db.SaveChangesAsync(ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "AWL seed done: tagged {Existing} existing words across {EntryCount} AWL families, created {Stubs} stubs, {Tags} WordTag rows, elapsed {Ms} ms.",
            taggedExisting, entries.Count, createdStubs, newTags.Count, sw.ElapsedMilliseconds);
    }

    public async Task SeedAvlIfEmptyAsync(CancellationToken ct = default)
    {
        if (await _db.Tags.AnyAsync(t => t.Code == "avl", ct))
        {
            _logger.LogInformation("AVL already seeded; skipping.");
            return;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Reading embedded AVL seed...");
        List<AvlEntry> entries;
        await using (var s = SeedManifest.OpenAvl())
            entries = AcademicSeedReader.ReadAvl(s);

        var avlTag = await GetOrCreateTagAsync("avl", "AVL", ct);
        var posByCode = await _db.PartsOfSpeech.ToDictionaryAsync(p => p.Code, p => p.Id, ct);
        var otherPosId = posByCode.TryGetValue("other", out var op)
            ? op
            : await GetOrCreatePartOfSpeechIdAsync("other", "Other", "Прочее", ct);

        // band buckets: каждый bucket = 5 AVL bands (~ну ~500 слов на bucket в начале)
        _bandTagCache.Clear();

        var existingByLemma = (await _db.Words
            .Select(w => new { w.Id, w.Lemma, w.PartOfSpeechId, w.PartOfSpeech.Code })
            .ToListAsync(ct))
            .GroupBy(x => x.Lemma, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var existingPairs = await _db.WordTags
            .Where(wt => wt.Tag.Code.StartsWith("avl"))
            .Select(wt => new { wt.WordId, wt.TagId })
            .ToListAsync(ct);
        var existingSet = new HashSet<(int, int)>(existingPairs.Select(p => (p.WordId, p.TagId)));

        var now = DateTime.UtcNow;
        var newWords = new List<Word>();
        var stubBand = new List<int>();
        var newTags = new List<WordTag>();
        var taggedExisting = 0;
        var createdStubs = 0;
        var seenStubKey = new HashSet<(string, int)>();

        const int AvlTopRank = 3000;

        foreach (var e in entries)
        {
            // фильтруем COCA-overflow: оставляем только классические top 3000 academic
            if (e.Frequency is null || e.Frequency > AvlTopRank) continue;

            var bandTagBucket = (e.Band - 1) / 5 + 1;
            if (!_bandTagCache.ContainsKey(bandTagBucket))
            {
                _bandTagCache[bandTagBucket] = await GetOrCreateTagAsync(
                    $"avl-band-{bandTagBucket}", $"AVL: банд {bandTagBucket}", ct);
            }
            var bTag = _bandTagCache[bandTagBucket];

            var ourPos = AvlPosMap.ToOurPos(e.PosCode);
            if (!posByCode.TryGetValue(ourPos, out var posId)) posId = otherPosId;

            if (existingByLemma.TryGetValue(e.Lemma, out var matched))
            {
                var preferred = matched.FirstOrDefault(m => m.Code == ourPos) ?? matched[0];
                if (existingSet.Add((preferred.Id, avlTag.Id)))
                    newTags.Add(new WordTag { WordId = preferred.Id, TagId = avlTag.Id });
                if (existingSet.Add((preferred.Id, bTag.Id)))
                    newTags.Add(new WordTag { WordId = preferred.Id, TagId = bTag.Id });
                taggedExisting++;
            }
            else
            {
                // (lemma, posId) уникальный ключ Word — избегаем коллизий
                if (!seenStubKey.Add((e.Lemma.ToLowerInvariant(), posId)))
                    continue;

                var stub = new Word
                {
                    Headword = e.Lemma,
                    Lemma = e.Lemma.ToLowerInvariant(),
                    Source = WordSource.Avl,
                    CefrLevel = CefrLevel.Unknown,
                    PartOfSpeechId = posId,
                    FrequencyRank = e.Frequency,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                newWords.Add(stub);
                stubBand.Add(bandTagBucket);
                createdStubs++;
            }
        }

        if (newWords.Count > 0)
        {
            _db.Words.AddRange(newWords);
            await _db.SaveChangesAsync(ct);
            for (var i = 0; i < newWords.Count; i++)
            {
                newTags.Add(new WordTag { WordId = newWords[i].Id, TagId = avlTag.Id });
                newTags.Add(new WordTag { WordId = newWords[i].Id, TagId = _bandTagCache[stubBand[i]].Id });
            }
        }

        if (newTags.Count > 0)
        {
            _db.WordTags.AddRange(newTags);
            await _db.SaveChangesAsync(ct);
        }

        sw.Stop();
        _logger.LogInformation(
            "AVL seed done: tagged {Existing} existing, created {Stubs} stubs across {EntryCount} AVL entries, {Tags} WordTag rows, elapsed {Ms} ms.",
            taggedExisting, createdStubs, entries.Count, newTags.Count, sw.ElapsedMilliseconds);
    }

    private readonly Dictionary<int, Tag> _bandTagCache = new();

    private async Task<Tag> GetOrCreateTagAsync(string code, string nameRu, CancellationToken ct)
    {
        var existing = await _db.Tags.FirstOrDefaultAsync(t => t.Code == code, ct);
        if (existing is not null) return existing;
        var t = new Tag { Code = code, NameRu = nameRu };
        _db.Tags.Add(t);
        await _db.SaveChangesAsync(ct);
        return t;
    }

    private async Task<int> GetOrCreatePartOfSpeechIdAsync(string code, string nameEn, string nameRu, CancellationToken ct)
    {
        var existing = await _db.PartsOfSpeech.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (existing is not null) return existing.Id;
        var pos = new PartOfSpeech { Code = code, NameEn = nameEn, NameRu = nameRu };
        _db.PartsOfSpeech.Add(pos);
        await _db.SaveChangesAsync(ct);
        return pos.Id;
    }

    private static void Add(Dictionary<string, List<int>> dict, string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<int>();
            dict[key] = list;
        }
        if (!list.Contains(value)) list.Add(value);
    }

    public async Task SeedIeltsCategoriesIfEmptyAsync(CancellationToken ct = default)
    {
        var topics = IeltsCategoriesSeed.Topics;
        var existingCodes = await _db.Categories
            .Where(c => c.Code.StartsWith("ielts-topic-"))
            .Select(c => c.Code)
            .ToListAsync(ct);
        if (existingCodes.Count == topics.Length)
        {
            _logger.LogInformation("IELTS categories already seeded; skipping.");
            return;
        }

        var existingSet = new HashSet<string>(existingCodes);
        var inserted = 0;
        foreach (var (code, nameEn, nameRu, order) in topics)
        {
            var fullCode = $"ielts-topic-{code}";
            if (existingSet.Contains(fullCode)) continue;
            _db.Categories.Add(new Category
            {
                Code = fullCode,
                NameEn = nameEn,
                NameRu = nameRu,
                OrderIndex = order,
            });
            inserted++;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("IELTS categories seeded: {Count} rows inserted.", inserted);
    }

    public async Task SeedPhaveIfEmptyAsync(CancellationToken ct = default)
    {
        if (await _db.PhrasalVerbs.AnyAsync(ct))
        {
            _logger.LogInformation("PHaVE already seeded; skipping.");
            return;
        }

        var phaveStream = _content.OpenJson(DictionaryContentFolder, "phave.json");
        if (phaveStream is null)
        {
            _logger.LogInformation("PHaVE: content not imported, skipping seed.");
            return;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Reading imported PHaVE seed...");

        PhaveSeedDocument doc;
        await using (phaveStream)
        {
            doc = await JsonSerializer.DeserializeAsync<PhaveSeedDocument>(phaveStream, cancellationToken: ct)
                  ?? throw new InvalidOperationException("PHaVE seed is empty/malformed.");
        }

        var pvPosId = await GetOrCreatePartOfSpeechIdAsync(
            "phrasal_verb", "Phrasal verb", "Фразовый глагол", ct);

        // base verb lookup by lemma (lowercase)
        var verbByLemma = await _db.Words
            .Where(w => w.PartOfSpeech.Code == "v")
            .Select(w => new { w.Id, w.Lemma })
            .ToDictionaryAsync(x => x.Lemma, x => x.Id, StringComparer.OrdinalIgnoreCase, ct);

        var phaveTag = await GetOrCreateTagAsync("phave", "PHaVE", ct);

        var prevAutoDetect = _db.ChangeTracker.AutoDetectChangesEnabled;
        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var now = DateTime.UtcNow;
            var createdVerbs = 0;
            var createdSenses = 0;
            foreach (var entry in doc.Entries)
            {
                var parts = entry.Phrase.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var basePart = parts[0].ToLowerInvariant();
                var particle = string.Join(' ', parts.Skip(1)).ToLowerInvariant();
                var headword = string.Join(' ', parts).ToLowerInvariant();

                int? baseWordId = verbByLemma.TryGetValue(basePart, out var bid) ? bid : null;

                var pv = new PhrasalVerb
                {
                    Headword = headword,
                    Lemma = headword,
                    BaseWordId = baseWordId,
                    Particle = particle,
                    CefrLevel = CefrLevel.Unknown,
                    Source = WordSource.Phave,
                    FrequencyRank = entry.Rank,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                var orderIdx = 0;
                foreach (var s in entry.Senses.OrderBy(x => x.Num))
                {
                    var sense = new Sense
                    {
                        DefinitionEn = string.IsNullOrWhiteSpace(s.Particle)
                            ? s.DefinitionEn
                            : $"({s.Particle}) {s.DefinitionEn}",
                        DefinitionRu = string.Empty,
                        OrderIndex = orderIdx,
                    };
                    if (!string.IsNullOrWhiteSpace(s.ExampleEn))
                    {
                        var ex = new Example
                        {
                            TextEn = s.ExampleEn,
                            Source = "phave",
                        };
                        sense.Examples.Add(ex);
                        pv.Examples.Add(ex);
                    }
                    pv.Senses.Add(sense);
                    orderIdx++;
                    createdSenses++;
                }

                _db.PhrasalVerbs.Add(pv);
                createdVerbs++;
            }

            _db.ChangeTracker.DetectChanges();
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "PHaVE seed done: created {Verbs} PhrasalVerb rows, {Senses} senses, elapsed {Ms} ms.",
                createdVerbs, createdSenses, sw.ElapsedMilliseconds);
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = prevAutoDetect;
        }
    }

    public async Task<int> BackfillAudioPathsAsync(CancellationToken ct = default)
    {
        var anyMissing = await _db.Words
            .AnyAsync(w => w.AudioUkPath == null && w.AudioUsPath == null, ct);
        if (!anyMissing)
        {
            return 0;
        }

        var stream = _content.OpenJson(DictionaryContentFolder, "oxford_5000.json");
        if (stream is null)
        {
            _logger.LogInformation("Backfill audio paths: Oxford content not imported, skipping.");
            return 0;
        }

        _logger.LogInformation("Backfilling audio paths from imported seed...");

        OxfordSeedDocument doc;
        await using (stream)
        {
            doc = await JsonSerializer.DeserializeAsync<OxfordSeedDocument>(stream,
                       cancellationToken: ct)
                  ?? throw new InvalidOperationException("Seed file is empty or malformed.");
        }

        var byHeadword = doc.Words
            .GroupBy(w => w.Headword.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        var updated = 0;
        const int pageSize = 500;
        var skip = 0;

        while (true)
        {
            var batch = await _db.Words
                .Where(w => w.AudioUkPath == null && w.AudioUsPath == null)
                .OrderBy(w => w.Id)
                .Take(pageSize)
                .ToListAsync(ct);

            if (batch.Count == 0) break;

            foreach (var w in batch)
            {
                var key = w.Headword.Trim().ToLowerInvariant();
                if (!byHeadword.TryGetValue(key, out var entry)) continue;

                var uk = NormalizeAudioPath(entry.AudioUk);
                var us = NormalizeAudioPath(entry.AudioUs);
                if (uk is null && us is null) continue;

                w.AudioUkPath = uk;
                w.AudioUsPath = us;
                updated++;
            }

            await _db.SaveChangesAsync(ct);
            skip += batch.Count;
            if (batch.Count < pageSize) break;
        }

        _logger.LogInformation("Backfill updated {Count} word(s).", updated);
        return updated;
    }
}
