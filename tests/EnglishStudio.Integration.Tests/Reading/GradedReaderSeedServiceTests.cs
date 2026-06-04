using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Seed;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class GradedReaderSeedServiceTests
{
    private static GradedReaderSeedService MakeSeeder(InMemoryReadingDb db) =>
        new(db.Factory, NullLogger<GradedReaderSeedService>.Instance);

    [Fact]
    public async Task Seeds_builtin_texts_from_embedded_json()
    {
        using var db = new InMemoryReadingDb();
        var added = await MakeSeeder(db).EnsureSeededAsync();

        Assert.True(added > 0);
        using var ctx = db.New();
        var builtin = ctx.ReadingTexts.Where(t => t.Source == ReadingSource.Builtin).ToList();
        Assert.Equal(added, builtin.Count);
        Assert.All(builtin, t => Assert.True(t.WordCount > 0));
        // A known seed entry carries a parsed CEFR.
        var morning = builtin.FirstOrDefault(t => t.Title == "My Morning");
        Assert.NotNull(morning);
        Assert.Equal(CefrLevel.A1, morning!.EstimatedCefr);
    }

    [Fact]
    public async Task Seeding_is_idempotent_across_instances()
    {
        using var db = new InMemoryReadingDb();
        var first = await MakeSeeder(db).EnsureSeededAsync();

        // A fresh instance (new _seeded flag) must not duplicate existing builtin rows.
        var second = await MakeSeeder(db).EnsureSeededAsync();

        Assert.True(first > 0);
        Assert.Equal(0, second);
        using var ctx = db.New();
        Assert.Equal(first, ctx.ReadingTexts.Count(t => t.Source == ReadingSource.Builtin));
    }

    [Fact]
    public async Task ListAsync_triggers_seeding()
    {
        using var db = new InMemoryReadingDb();
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var library = new TextLibraryService(db.Factory, scopeFactory, MakeSeeder(db), NullLogger<TextLibraryService>.Instance);

        var items = await library.ListAsync();

        Assert.Contains(items, i => i.Source == ReadingSource.Builtin);
    }
}
