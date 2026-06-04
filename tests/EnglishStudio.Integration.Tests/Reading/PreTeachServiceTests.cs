using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class PreTeachServiceTests
{
    private const int TextId = 1;

    private static PreTeachService Make(
        InMemoryDictionaryDb dict, string body, FakeSrsService srs, FakeEnrichmentService enrich) =>
        new(dict.ScopeFactory, new FakeTextLibraryService(TextId, body), srs, enrich,
            NullLogger<PreTeachService>.Instance);

    [Fact]
    public async Task Drops_stop_words_and_counts_occurrences()
    {
        using var dict = new InMemoryDictionaryDb(); // empty dictionary
        var svc = Make(dict, "The cat and the philosophy of philosophy is here.",
            new FakeSrsService(), new FakeEnrichmentService());

        var result = await svc.AnalyzeAsync(TextId);

        var words = result.Candidates.Select(c => c.Headword).ToList();
        Assert.DoesNotContain("the", words);
        Assert.DoesNotContain("and", words);
        Assert.DoesNotContain("is", words);
        Assert.Contains("philosophy", words);
        Assert.Contains("cat", words);

        var philosophy = result.Candidates.First(c => c.Headword == "philosophy");
        Assert.Equal(2, philosophy.Occurrences);
        Assert.False(philosophy.InDictionary);
        Assert.Null(philosophy.WordId);
    }

    [Fact]
    public async Task Respects_max_words_cap()
    {
        using var dict = new InMemoryDictionaryDb();
        var svc = Make(dict, "alpha bravo charlie delta echo foxtrot golf hotel india juliet",
            new FakeSrsService(), new FakeEnrichmentService());

        var result = await svc.AnalyzeAsync(TextId, new PreTeachOptions(MaxWords: 3));

        Assert.Equal(3, result.Candidates.Count);
        Assert.Equal(10, result.TotalDistinctWords);
    }

    [Fact]
    public async Task Filters_by_cefr_and_training_state_and_ranks_unknown_first()
    {
        using var dict = new InMemoryDictionaryDb();
        SeedWord(dict, "house", CefrLevel.A1, "дом");        // below B1 → known, not a candidate
        SeedWord(dict, "philosophy", CefrLevel.C1, "философия"); // ≥ B1 → candidate
        SeedWord(dict, "garden", CefrLevel.B2, "сад");       // ≥ B1 but already in training → excluded

        var gardenId = WordId(dict, "garden");
        var srs = new FakeSrsService();
        srs.AlreadyTraining.Add(gardenId);

        var svc = Make(dict, "The house has a garden and deep philosophy about zephyrology.",
            srs, new FakeEnrichmentService());

        var result = await svc.AnalyzeAsync(TextId);
        var byWord = result.Candidates.ToDictionary(c => c.Headword, StringComparer.OrdinalIgnoreCase);

        Assert.False(byWord.ContainsKey("house"));   // too easy
        Assert.False(byWord.ContainsKey("garden"));  // already learning

        Assert.True(byWord.ContainsKey("philosophy"));
        Assert.True(byWord["philosophy"].InDictionary);
        Assert.Equal("философия", byWord["philosophy"].TranslationRu);
        Assert.Equal(CefrLevel.C1, byWord["philosophy"].Cefr);

        Assert.True(byWord.ContainsKey("zephyrology"));
        Assert.Null(byWord["zephyrology"].WordId);

        // house (below threshold) + garden (in training) both count as "known".
        Assert.Equal(2, result.KnownCount);

        // Unknown word ranks ahead of the in-dictionary one.
        var order = result.Candidates.Select(c => c.Headword).ToList();
        Assert.True(order.IndexOf("zephyrology") < order.IndexOf("philosophy"));
    }

    [Fact]
    public async Task AddToTraining_enriches_unknown_and_adds_all()
    {
        using var dict = new InMemoryDictionaryDb();
        var srs = new FakeSrsService();
        var enrich = new FakeEnrichmentService { IsAvailable = true };
        var svc = Make(dict, "x", srs, enrich);

        var candidates = new[]
        {
            new PreTeachCandidate("zephyr", "zephyr", null, CefrLevel.Unknown, null, false, false, 1),
            new PreTeachCandidate("philosophy", "philosophy", "философия", CefrLevel.C1, 42, true, false, 1),
        };

        var added = await svc.AddToTrainingAsync(candidates);

        Assert.Equal(2, added);
        Assert.Contains("zephyr", enrich.Enriched);
        Assert.Contains(42, srs.AddedWordIds);
        Assert.Equal(2, srs.AddedWordIds.Count);
    }

    [Fact]
    public async Task AddToTraining_offline_skips_unknown_words()
    {
        using var dict = new InMemoryDictionaryDb();
        var srs = new FakeSrsService();
        var enrich = new FakeEnrichmentService { IsAvailable = false };
        var svc = Make(dict, "x", srs, enrich);

        var candidates = new[]
        {
            new PreTeachCandidate("zephyr", "zephyr", null, CefrLevel.Unknown, null, false, false, 1),
            new PreTeachCandidate("philosophy", "philosophy", "философия", CefrLevel.C1, 42, true, false, 1),
        };

        var added = await svc.AddToTrainingAsync(candidates);

        Assert.Equal(1, added);                       // only the known one
        Assert.Empty(enrich.Enriched);
        Assert.Equal(new[] { 42 }, srs.AddedWordIds);
    }

    private static void SeedWord(InMemoryDictionaryDb dict, string lemma, CefrLevel cefr, string translation)
    {
        dict.Seed(db =>
        {
            var pos = db.PartsOfSpeech.FirstOrDefault(p => p.Code == "n");
            if (pos is null)
            {
                pos = new PartOfSpeech { Code = "n", NameEn = "noun", NameRu = "существительное" };
                db.PartsOfSpeech.Add(pos);
                db.SaveChanges();
            }

            var now = DateTime.UtcNow;
            var word = new Word
            {
                Headword = lemma, Lemma = lemma, CefrLevel = cefr,
                Source = WordSource.Seed, PartOfSpeechId = pos.Id, CreatedAt = now, UpdatedAt = now
            };
            word.Senses.Add(new Sense
            {
                DefinitionEn = "", DefinitionRu = "", OrderIndex = 0,
                Translations = { new Translation { TextRu = translation, OrderIndex = 0 } }
            });
            db.Words.Add(word);
        });
    }

    private static int WordId(InMemoryDictionaryDb dict, string lemma)
    {
        using var scope = dict.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnglishStudio.Modules.Dictionary.Data.DictionaryDbContext>();
        return db.Words.First(w => w.Lemma == lemma).Id;
    }
}
