using System.Linq;
using EnglishStudio.Integration.Tests.Infrastructure;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Content;

/// <summary>Drives a real seed service against a content file on disk (no embedded resources).</summary>
[Collection("ContentIO")]
public sealed class SeedFromFileTests
{
    private static ReadingSeedService MakeSeeder(SqliteInMemoryDb db) =>
        new(db.Factory, new FileSystemContentStore(), NullLogger<ReadingSeedService>.Instance);

    [Fact]
    public async Task ReadingSeed_imports_from_content_file()
    {
        using var env = new ContentTestEnv();
        using var db = new SqliteInMemoryDb();
        env.WriteContentFile("Reading", "ielts_reading_tests.json", PackFixtures.ReadingJson);

        await MakeSeeder(db).SeedIfMissingAsync();

        using var ctx = db.NewContext();
        Assert.Contains(ctx.TestSets, t => t.Code == "test-reading-1");
    }

    [Fact]
    public async Task ReadingSeed_is_soft_noop_without_content()
    {
        using var env = new ContentTestEnv();   // empty content root → IsImported == false
        using var db = new SqliteInMemoryDb();

        await MakeSeeder(db).SeedIfMissingAsync();   // must not throw

        using var ctx = db.NewContext();
        Assert.Empty(ctx.TestSets);
    }

    [Fact]
    public async Task ReadingSeed_is_idempotent_across_instances()
    {
        using var env = new ContentTestEnv();
        using var db = new SqliteInMemoryDb();
        env.WriteContentFile("Reading", "ielts_reading_tests.json", PackFixtures.ReadingJson);

        await MakeSeeder(db).SeedIfMissingAsync();
        await MakeSeeder(db).SeedIfMissingAsync();   // second run is a no-op

        using var ctx = db.NewContext();
        Assert.Equal(1, ctx.TestSets.Count(t => t.Code == "test-reading-1"));
    }
}
