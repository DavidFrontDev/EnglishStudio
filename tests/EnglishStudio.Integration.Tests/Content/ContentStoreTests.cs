using System.IO;
using EnglishStudio.Modules.Dictionary.Content;
using Xunit;

namespace EnglishStudio.Integration.Tests.Content;

[Collection("ContentIO")]
public sealed class ContentStoreTests
{
    [Fact]
    public void IsImported_is_false_on_empty_content_root()
    {
        using var env = new ContentTestEnv();
        var store = new FileSystemContentStore();

        Assert.False(store.IsImported(ContentSection.Reading));
        Assert.False(store.IsImported(ContentSection.Listening));
        Assert.False(store.IsImported(ContentSection.Writing));
        Assert.False(store.IsImported(ContentSection.DictionaryOxford));
        Assert.False(store.IsImported(ContentSection.DictionaryPhave));
        Assert.False(store.IsImported(ContentSection.Speaking));
    }

    [Fact]
    public void IsImported_turns_true_once_key_file_is_present()
    {
        using var env = new ContentTestEnv();
        var store = new FileSystemContentStore();

        env.WriteContentFile("Reading", "ielts_reading_tests.json", "[]");
        env.WriteContentFile("Speaking", Path.Combine("Ielts 15", "Test№1.txt"), "hi");

        Assert.True(store.IsImported(ContentSection.Reading));
        Assert.True(store.IsImported(ContentSection.Speaking));
        Assert.False(store.IsImported(ContentSection.Writing));
    }

    [Fact]
    public void OpenJson_and_ResolveFile_round_trip()
    {
        using var env = new ContentTestEnv();
        var store = new FileSystemContentStore();

        env.WriteContentFile("Reading", "ielts_reading_tests.json", "[1,2,3]");
        env.WriteContentFile("Reading", Path.Combine("test-x", "map.png"), "img-bytes");

        using (var s = store.OpenJson("Reading", "ielts_reading_tests.json"))
        {
            Assert.NotNull(s);
            using var reader = new StreamReader(s!);
            Assert.Equal("[1,2,3]", reader.ReadToEnd());
        }

        Assert.Null(store.OpenJson("Reading", "does_not_exist.json"));

        Assert.NotNull(store.ResolveFile("Reading", "test-x", "map.png"));
        Assert.Null(store.ResolveFile("Reading", "test-x", "missing.png"));
    }

    [Fact]
    public void ReadManifest_parses_pack_version_and_sections()
    {
        using var env = new ContentTestEnv();
        var store = new FileSystemContentStore();

        Assert.Null(store.ReadManifest());

        File.WriteAllText(Path.Combine(env.ContentRoot, "manifest.json"), """
            { "packVersion": 1, "createdAt": "2026-06-03",
              "sections": { "reading": true, "writing": false } }
            """);

        var manifest = store.ReadManifest();
        Assert.NotNull(manifest);
        Assert.Equal(1, manifest!.PackVersion);
        Assert.True(manifest.Has(ContentSection.Reading));
        Assert.False(manifest.Has(ContentSection.Writing));
        Assert.False(manifest.Has(ContentSection.Listening)); // absent key → not imported
    }
}
