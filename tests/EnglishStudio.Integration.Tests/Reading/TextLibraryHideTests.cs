using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Seed;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

/// <summary>Soft-hide round-trip for the reading library (used to hide built-in graded readers).</summary>
public class TextLibraryHideTests
{
    private static TextLibraryService MakeService(InMemoryReadingDb db)
    {
        // EstimateCefrAsync resolves DictionaryDbContext from this (empty) scope and soft-fails to
        // Unknown — fine here, we're only exercising the hide flag.
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var seeder = new GradedReaderSeedService(db.Factory, NullLogger<GradedReaderSeedService>.Instance);
        return new TextLibraryService(db.Factory, scopeFactory, seeder, NullLogger<TextLibraryService>.Instance);
    }

    [Fact]
    public async Task SetHiddenAsync_hides_and_restores()
    {
        using var db = new InMemoryReadingDb();
        var svc = MakeService(db);
        var id = await svc.AddAsync("Sample", "hello world", ReadingSource.Builtin);

        await svc.SetHiddenAsync(id, true);
        using (var ctx = db.New())
            Assert.True(ctx.ReadingTexts.Find(id)!.IsHidden);

        await svc.SetHiddenAsync(id, false);
        using (var ctx = db.New())
            Assert.False(ctx.ReadingTexts.Find(id)!.IsHidden);
    }

    [Fact]
    public async Task ListAsync_surfaces_the_hidden_flag()
    {
        using var db = new InMemoryReadingDb();
        var svc = MakeService(db);
        var id = await svc.AddAsync("Sample", "hello world", ReadingSource.Builtin);
        await svc.SetHiddenAsync(id, true);

        var item = (await svc.ListAsync()).First(t => t.Id == id);
        Assert.True(item.IsHidden);   // ListAsync returns hidden too; the VM filters them client-side
    }
}
