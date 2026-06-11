using System.IO;
using System.IO.Compression;
using System.Linq;
using EnglishStudio.Content;
using EnglishStudio.Modules.Ai.Rubrics;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Seed;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Listening.Seed;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;
using EnglishStudio.Modules.Ielts.Writing.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Content;

/// <summary>
/// End-to-end import: a mini content-pack (folder + zip) → ContentImportService.ImportAsync →
/// files land in ContentRoot and every section's DB is seeded, idempotently. Wires the real seed
/// services over file-backed SQLite under the temp AppData root.
/// </summary>
[Collection("ContentIO")]
public sealed class ContentImportServiceTests
{
    private const string ManifestJson = """
        { "packVersion": 1, "createdAt": "2026-06-03",
          "sections": { "reading": true, "listening": true, "writing": true,
                        "dictionaryOxford": true, "dictionaryPhave": true, "speaking": false,
                        "rubrics": true } }
        """;

    [Fact]
    public async Task Import_from_folder_copies_files_and_seeds_every_section()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        var pack = env.NewPackDir();
        WritePack(pack);

        var collector = new Collector();
        var result = await sp.GetRequiredService<IContentImportService>().ImportAsync(pack, collector);

        Assert.True(result.Success, string.Join("; ", result.Errors));

        // Files copied into ContentRoot.
        Assert.True(File.Exists(Path.Combine(env.ContentRoot, "Reading", "ielts_reading_tests.json")));
        Assert.True(File.Exists(Path.Combine(env.ContentRoot, "Dictionary", "oxford_5000.json")));

        // Rubrics externalized into the pack are now loadable via RubricLoader (no longer embedded).
        Assert.True(RubricLoader.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(RubricLoader.Writing));

        // Progress ran to completion.
        Assert.Contains(collector.Items, p => p.Stage == "done");
        Assert.Equal(1.0, collector.Items[^1].Fraction, 3);

        // DB seeded across modules.
        var ieltsFactory = sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
        await using (var idb = await ieltsFactory.CreateDbContextAsync())
        {
            Assert.Contains(idb.TestSets, t => t.Code == "test-reading-1");
            Assert.Contains(idb.TestSets, t => t.Code == "test-listening-1");
            Assert.Contains(idb.TestSets, t => t.Code == "test-writing-1");
        }

        using var scope = sp.CreateScope();
        var dict = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        Assert.Equal(2, dict.Words.Count());        // apple, run
        Assert.True(dict.PhrasalVerbs.Any());        // give up

        // Summary reflects the imported sections (speaking excluded by manifest).
        Assert.Contains(result.Sections, s => s.Section == ContentSection.Reading && s.Reseeded);
        Assert.DoesNotContain(result.Sections, s => s.Section == ContentSection.Speaking);
    }

    [Fact]
    public async Task Import_from_zip_works()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        var packDir = env.NewPackDir();
        WritePack(packDir);
        var zipPath = Path.Combine(env.Root, "pack.zip");
        ZipFile.CreateFromDirectory(packDir, zipPath);

        var result = await sp.GetRequiredService<IContentImportService>().ImportAsync(zipPath);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var ieltsFactory = sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
        await using var idb = await ieltsFactory.CreateDbContextAsync();
        Assert.Contains(idb.TestSets, t => t.Code == "test-reading-1");
    }

    [Fact]
    public async Task Import_is_idempotent()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        var pack = env.NewPackDir();
        WritePack(pack);
        var import = sp.GetRequiredService<IContentImportService>();

        await import.ImportAsync(pack);
        await import.ImportAsync(pack);   // second pass must not duplicate

        var ieltsFactory = sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
        await using var idb = await ieltsFactory.CreateDbContextAsync();
        Assert.Equal(1, idb.TestSets.Count(t => t.Code == "test-reading-1"));

        using var scope = sp.CreateScope();
        var dict = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
        Assert.Equal(2, dict.Words.Count());
    }

    [Fact]
    public void PeekManifest_reads_folder_and_zip()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();

        var pack = env.NewPackDir();
        WritePack(pack);
        var import = sp.GetRequiredService<IContentImportService>();

        var fromFolder = import.PeekManifest(pack);
        Assert.NotNull(fromFolder);
        Assert.True(fromFolder!.Has(ContentSection.Reading));

        var zip = Path.Combine(env.Root, "peek.zip");
        ZipFile.CreateFromDirectory(pack, zip);
        var fromZip = import.PeekManifest(zip);
        Assert.NotNull(fromZip);
        Assert.True(fromZip!.Has(ContentSection.Writing));
    }

    [Fact]
    public async Task Import_without_manifest_fails()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        var pack = env.NewPackDir();
        var readingDir = Directory.CreateDirectory(Path.Combine(pack, "Reading"));
        File.WriteAllText(Path.Combine(readingDir.FullName, "ielts_reading_tests.json"), PackFixtures.ReadingJson);

        var result = await sp.GetRequiredService<IContentImportService>().ImportAsync(pack);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task Import_with_missing_section_file_fails_before_any_copy()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        // Manifest claims reading + writing, but only writing's key file is present.
        var pack = env.NewPackDir();
        File.WriteAllText(Path.Combine(pack, "manifest.json"), """
            { "packVersion": 1, "createdAt": "2026-06-03",
              "sections": { "reading": true, "writing": true } }
            """);
        Directory.CreateDirectory(Path.Combine(pack, "Writing"));
        File.WriteAllText(Path.Combine(pack, "Writing", "writing_tests.json"), PackFixtures.WritingJson);

        var result = await sp.GetRequiredService<IContentImportService>().ImportAsync(pack);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("reading"));
        Assert.Empty(result.Sections);

        // Fail-fast: nothing copied (not even the valid writing file) and the DB is untouched.
        Assert.False(File.Exists(Path.Combine(env.ContentRoot, "Writing", "writing_tests.json")));
        var ieltsFactory = sp.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
        await using var idb = await ieltsFactory.CreateDbContextAsync();
        Assert.Empty(idb.TestSets);
    }

    [Fact]
    public async Task Import_with_unsupported_pack_version_is_rejected()
    {
        using var env = new ContentTestEnv();
        var sp = BuildProvider();
        await MigrateAsync(sp);

        // A fully valid pack, but the manifest declares a future packVersion.
        var pack = env.NewPackDir();
        WritePack(pack);
        File.WriteAllText(Path.Combine(pack, "manifest.json"), """
            { "packVersion": 999, "createdAt": "2026-06-03",
              "sections": { "reading": true } }
            """);

        var result = await sp.GetRequiredService<IContentImportService>().ImportAsync(pack);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Content_ErrUnsupportedVersion"));

        // Rejected before copy despite valid section files being present.
        Assert.False(File.Exists(Path.Combine(env.ContentRoot, "Reading", "ielts_reading_tests.json")));
    }

    // ── wiring ──────────────────────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IContentStore, FileSystemContentStore>();

        services.AddDbContext<DictionaryDbContext>(o => o.UseSqlite(DictionaryPaths.SqliteConnectionString));
        services.AddDbContextFactory<IeltsDbContext>(o => o.UseSqlite(
            DictionaryPaths.SqliteConnectionString,
            b => b.MigrationsHistoryTable("__EFMigrationsHistory_Ielts")));

        services.AddScoped<SeedService>();
        services.AddSingleton<ReadingSeedService>();
        services.AddSingleton<ListeningSeedService>();
        services.AddSingleton<WritingSeedService>();
        services.AddSingleton<CambridgeSpeakingTestParser>();
        services.AddSingleton(s => new CambridgeSpeakingImportService(
            s.GetRequiredService<IDbContextFactory<IeltsDbContext>>(),
            s.GetRequiredService<CambridgeSpeakingTestParser>(),
            s.GetRequiredService<ILogger<CambridgeSpeakingImportService>>(),
            Path.Combine(DictionaryPaths.IeltsContentRoot, "Speaking")));

        services.AddContentModule();
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DictionaryDbContext>().Database.MigrateAsync();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
        await using var idb = await factory.CreateDbContextAsync();
        await idb.Database.MigrateAsync();
    }

    private static void WritePack(string dir)
    {
        void W(string rel, string text)
        {
            var p = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, text);
        }

        W(Path.Combine("Reading", "ielts_reading_tests.json"), PackFixtures.ReadingJson);
        W(Path.Combine("Listening", "ielts_listening_tests.json"), PackFixtures.ListeningJson);
        W(Path.Combine("Writing", "writing_tests.json"), PackFixtures.WritingJson);
        W(Path.Combine("Dictionary", "oxford_5000.json"), PackFixtures.OxfordJson);
        W(Path.Combine("Dictionary", "phave.json"), PackFixtures.PhaveJson);
        W(Path.Combine("Rubrics", "IeltsRubric_Writing.md"), PackFixtures.RubricWritingMd);
        W(Path.Combine("Rubrics", "IeltsRubric_Speaking.md"), PackFixtures.RubricSpeakingMd);
        W("manifest.json", ManifestJson);
    }

    /// <summary>Synchronous progress collector — ImportAsync calls Report() inline, so it's race-free.</summary>
    private sealed class Collector : IProgress<ImportProgress>
    {
        public readonly List<ImportProgress> Items = new();
        public void Report(ImportProgress value) => Items.Add(value);
    }
}
