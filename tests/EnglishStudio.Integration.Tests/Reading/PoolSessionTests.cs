using System.Linq;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Srs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>
/// The text-scoped FSRS session (SrsService.BuildSessionForWordIdsAsync) used by the trainer's
/// "повторить пул" — returns only the pool's words, due-soonest first.
/// </summary>
public class PoolSessionTests
{
    private sealed class NoopScheduler : IFsrsScheduler
    {
        public ReviewLog InitializeFromFirstReview(UserWordProgress p, SrsRating r, DateTime n) => new();
        public ReviewLog Schedule(UserWordProgress p, SrsRating r, DateTime n) => new();
    }

    [Fact]
    public async Task BuildSessionForWordIds_returns_only_pool_words_due_first()
    {
        using var dict = new InMemoryDictionaryDb();
        var now = DateTime.UtcNow;
        int wA = 0, wB = 0, wC = 0;

        dict.Seed(db =>
        {
            var pos = new PartOfSpeech { Code = "n", NameEn = "Noun", NameRu = "Сущ" };
            db.PartsOfSpeech.Add(pos);
            db.SaveChanges();

            Word W(string h) => new()
            {
                Headword = h, Lemma = h, PartOfSpeechId = pos.Id,
                Source = WordSource.Seed, CefrLevel = CefrLevel.Unknown,
                CreatedAt = now, UpdatedAt = now
            };
            var a = W("alpha"); var b = W("beta"); var c = W("gamma");
            db.Words.AddRange(a, b, c);
            db.SaveChanges();
            wA = a.Id; wB = b.Id; wC = c.Id;

            db.UserWordProgress.AddRange(
                new UserWordProgress { WordId = wA, State = SrsState.New, CreatedAt = now, UpdatedAt = now },
                new UserWordProgress { WordId = wB, State = SrsState.Review, NextReviewAt = now.AddDays(-1), CreatedAt = now, UpdatedAt = now },
                new UserWordProgress { WordId = wC, State = SrsState.New, CreatedAt = now, UpdatedAt = now });
        });

        var srs = new SrsService(dict.ScopeFactory, new NoopScheduler(), NullLogger<SrsService>.Instance);

        var session = await srs.BuildSessionForWordIdsAsync(new[] { wA, wB }, now);

        Assert.Equal(2, session.Count);                                  // only the requested pool words
        Assert.DoesNotContain(session, p => p.WordId == wC);
        Assert.Equal(wB, session[0].WordId);                             // due card first, new card last
    }

    [Fact]
    public async Task BuildSessionForWordIds_is_empty_for_empty_pool()
    {
        using var dict = new InMemoryDictionaryDb();
        var srs = new SrsService(dict.ScopeFactory, new NoopScheduler(), NullLogger<SrsService>.Instance);
        Assert.Empty(await srs.BuildSessionForWordIdsAsync(Array.Empty<int>(), DateTime.UtcNow));
    }
}
