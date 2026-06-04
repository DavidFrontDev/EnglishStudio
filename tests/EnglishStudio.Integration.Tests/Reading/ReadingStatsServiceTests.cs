using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class ReadingStatsServiceTests
{
    private static ReadingStatsService Make(InMemoryReadingDb rdb, InMemoryDictionaryDb dict) =>
        new(rdb.Factory, dict.ScopeFactory, NullLogger<ReadingStatsService>.Instance);

    private static int SeedText(InMemoryReadingDb db, string body = "body")
    {
        using var ctx = db.New();
        var t = new ReadingText { Title = "T", BodyText = body, CreatedAt = DateTime.UtcNow };
        ctx.ReadingTexts.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    private static void AddSession(InMemoryReadingDb db, int textId, DateTime when, double wpm, double acc, int words, int durationSec, bool completed)
    {
        using var ctx = db.New();
        ctx.ReadingSessions.Add(new ReadingSession
        {
            ReadingTextId = textId, StartedAt = when, Wpm = wpm, AccuracyPct = acc,
            WordsRead = words, DurationSec = durationSec, Completed = completed
        });
        ctx.SaveChanges();
    }

    [Fact]
    public async Task Summary_aggregates_completed_sessions_only()
    {
        using var rdb = new InMemoryReadingDb();
        using var dict = new InMemoryDictionaryDb();
        var textId = SeedText(rdb);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AddSession(rdb, textId, t0, wpm: 100, acc: 90, words: 200, durationSec: 120, completed: true);
        AddSession(rdb, textId, t0.AddDays(1), wpm: 140, acc: 95, words: 300, durationSec: 129, completed: true);
        AddSession(rdb, textId, t0.AddDays(2), wpm: 50, acc: 50, words: 50, durationSec: 60, completed: false); // ignored

        var summary = await Make(rdb, dict).GetSummaryAsync();

        Assert.Equal(2, summary.SessionsTotal);
        Assert.Equal(500, summary.WordsReadTotal);
        Assert.Equal(120, summary.AvgWpm);
        Assert.Equal(140, summary.BestWpm);
        Assert.Equal(4, summary.MinutesReadTotal); // (120+129)/60 ≈ 4.15 → 4
        Assert.Equal(2, summary.SpeedTrend.Count);
        Assert.Equal(100, summary.SpeedTrend[0].Wpm); // ordered by date
    }

    [Fact]
    public async Task Summary_empty_when_no_sessions()
    {
        using var rdb = new InMemoryReadingDb();
        using var dict = new InMemoryDictionaryDb();
        var summary = await Make(rdb, dict).GetSummaryAsync();
        Assert.Equal(0, summary.SessionsTotal);
        Assert.Empty(summary.SpeedTrend);
    }

    [Fact]
    public async Task SpeedTrend_filters_by_text()
    {
        using var rdb = new InMemoryReadingDb();
        using var dict = new InMemoryDictionaryDb();
        var a = SeedText(rdb);
        var b = SeedText(rdb);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        AddSession(rdb, a, t0, 100, 90, 100, 60, true);
        AddSession(rdb, b, t0.AddDays(1), 120, 92, 100, 60, true);

        var trendA = await Make(rdb, dict).GetSpeedTrendAsync(a);
        var point = Assert.Single(trendA);
        Assert.Equal(100, point.Wpm);
    }

    [Fact]
    public async Task Coverage_counts_known_by_srs_or_elementary_cefr()
    {
        using var rdb = new InMemoryReadingDb();
        using var dict = new InMemoryDictionaryDb();
        SeedText(rdb, "Philosophy in a house with quantum ideas.");

        dict.Seed(db =>
        {
            var pos = new PartOfSpeech { Code = "n", NameEn = "noun", NameRu = "сущ" };
            db.PartsOfSpeech.Add(pos);
            db.SaveChanges();

            var now = DateTime.UtcNow;
            var house = new Word { Headword = "house", Lemma = "house", CefrLevel = CefrLevel.A1, Source = WordSource.Seed, PartOfSpeechId = pos.Id, CreatedAt = now, UpdatedAt = now };
            var phil = new Word { Headword = "philosophy", Lemma = "philosophy", CefrLevel = CefrLevel.C1, Source = WordSource.Seed, PartOfSpeechId = pos.Id, CreatedAt = now, UpdatedAt = now };
            db.Words.AddRange(house, phil);
            db.SaveChanges();

            // philosophy is in SRS → known despite C1; quantum is absent → unknown.
            db.UserWordProgress.Add(new UserWordProgress { WordId = phil.Id, State = SrsState.New, CreatedAt = now, UpdatedAt = now });
        });

        var coverage = await Make(rdb, dict).GetCoverageAsync();

        var c = Assert.Single(coverage);
        // content words: philosophy, house, quantum, ideas → 4 distinct (stopwords in/a/with dropped)
        Assert.Equal(4, c.TotalWords);
        Assert.Equal(2, c.KnownWords);           // house (A1) + philosophy (SRS)
        Assert.Equal(50.0, c.CoveragePct);
    }
}
