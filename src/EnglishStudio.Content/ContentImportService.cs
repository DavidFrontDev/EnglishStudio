using System.IO;
using System.IO.Compression;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Dictionary.Seed;
using EnglishStudio.Modules.Ielts.Listening.Seed;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;
using EnglishStudio.Modules.Ielts.Writing.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Content;

/// <summary>
/// Imports a content-pack (folder or .zip) into <see cref="IContentStore.ContentRoot"/> and
/// (re)seeds every affected module. The only component aware of all seed services at once
/// (see plans/Infra_Publish_GitHub_AgentExecution.md §1.2, §A5).
/// </summary>
public sealed class ContentImportService : IContentImportService
{
    /// <summary>Highest content-pack format version this build understands. Packs declaring a higher
    /// <c>packVersion</c> are rejected before any copy/seed (see docs/CONTENT_PACK.md).</summary>
    public const int SupportedPackVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IContentStore _content;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMessageLocalizer _messages;
    private readonly ILogger<ContentImportService> _log;

    public ContentImportService(
        IContentStore content,
        IServiceScopeFactory scopeFactory,
        IMessageLocalizer messages,
        ILogger<ContentImportService> log)
    {
        _content = content;
        _scopeFactory = scopeFactory;
        _messages = messages;
        _log = log;
    }

    public ContentManifest? PeekManifest(string packPathFolderOrZip)
    {
        if (IsZip(packPathFolderOrZip))
            return ReadManifestFromZip(packPathFolderOrZip);

        var p = Path.Combine(packPathFolderOrZip, "manifest.json");
        return File.Exists(p)
            ? JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(p), JsonOpts)
            : null;
    }

    public async Task<ImportResult> ImportAsync(
        string packPathFolderOrZip,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        string? tempDir = null;
        try
        {
            progress?.Report(new ImportProgress("validate", 0, 0, 0, null));

            string packFolder;
            if (IsZip(packPathFolderOrZip))
            {
                tempDir = Path.Combine(Path.GetTempPath(), "es-import-" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(packPathFolderOrZip, tempDir);
                packFolder = ResolvePackRoot(tempDir);
            }
            else
            {
                packFolder = packPathFolderOrZip;
            }

            // 1. Manifest is mandatory.
            var manifest = ReadManifestFromFolder(packFolder);
            if (manifest is null)
            {
                return new ImportResult(
                    false,
                    Array.Empty<ImportedSection>(),
                    new[] { _messages.Format("Content_ErrManifestMissing") });
            }

            var errors = new List<string>();

            // 2a. Reject packs newer than this build supports — before touching anything.
            if (manifest.PackVersion > SupportedPackVersion)
            {
                errors.Add(_messages.Format("Content_ErrUnsupportedVersion", manifest.PackVersion, SupportedPackVersion));
            }

            // 2b. Validate: every section flagged true must carry its key file.
            foreach (var section in Enum.GetValues<ContentSection>())
            {
                if (!manifest.Has(section)) continue;
                if (!SectionKeyFileExists(packFolder, section))
                {
                    errors.Add(_messages.Format("Content_ErrSectionFileMissing", ContentManifest.KeyOf(section)));
                }
            }

            // 2c. FAIL-FAST: abort BEFORE copying or seeding if validation found anything, so an
            //     invalid pack can never partially overwrite the user's content or database.
            if (errors.Count > 0)
            {
                _log.LogWarning("Content import aborted on validation ({Count} error(s)); nothing was copied.", errors.Count);
                return new ImportResult(false, Array.Empty<ImportedSection>(), errors);
            }

            // 3. Sum bytes for byte-progress.
            var allFiles = Directory.GetFiles(packFolder, "*", SearchOption.AllDirectories);
            long bytesTotal = allFiles.Sum(f => new FileInfo(f).Length);

            // 4. Copy pack → ContentRoot (recursive, overwrite), reporting per-file byte progress.
            var destRoot = _content.ContentRoot;
            Directory.CreateDirectory(destRoot);
            long bytesDone = 0;
            foreach (var src in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(packFolder, src);
                var dest = Path.Combine(destRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                bytesDone += new FileInfo(src).Length;
                progress?.Report(new ImportProgress(
                    "copy", bytesDone, bytesTotal,
                    bytesTotal == 0 ? 1 : bytesDone / (double)bytesTotal, rel));
            }

            // 5. Re-seed affected modules in a fresh scope. Each seeder is idempotent and
            //    soft-skips a section that wasn't imported; a single failure is recorded but
            //    doesn't abort the rest.
            var reseeded = new HashSet<ContentSection>();
            using (var scope = _scopeFactory.CreateScope())
            {
                var sp = scope.ServiceProvider;

                async Task Seed(ContentSection section, Func<Task> run)
                {
                    if (!manifest.Has(section)) return;
                    progress?.Report(new ImportProgress(
                        $"seed:{section}", bytesTotal, bytesTotal, 1, null));
                    try
                    {
                        await run();
                        reseeded.Add(section);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Content import: re-seed failed for {Section}.", section);
                        errors.Add(_messages.Format("Content_ErrReseedFailed", ContentManifest.KeyOf(section), ex.Message));
                    }
                }

                await Seed(ContentSection.Reading,
                    () => sp.GetRequiredService<ReadingSeedService>().SeedIfMissingAsync(ct));
                await Seed(ContentSection.Listening,
                    () => sp.GetRequiredService<ListeningSeedService>().SeedIfMissingAsync(ct));
                await Seed(ContentSection.Writing,
                    () => sp.GetRequiredService<WritingSeedService>().SeedIfMissingAsync(ct));
                await Seed(ContentSection.Speaking,
                    () => sp.GetRequiredService<CambridgeSpeakingImportService>().ImportIfPossibleAsync(ct));

                var seed = sp.GetRequiredService<SeedService>();
                await Seed(ContentSection.DictionaryOxford, async () =>
                {
                    await seed.SeedIfEmptyAsync(ct);
                    await seed.BackfillAudioPathsAsync(ct);
                });
                await Seed(ContentSection.DictionaryPhave,
                    () => seed.SeedPhaveIfEmptyAsync(ct));
            }

            // 6. Summary per manifest section.
            var sections = new List<ImportedSection>();
            foreach (var section in Enum.GetValues<ContentSection>())
            {
                if (!manifest.Has(section)) continue;
                sections.Add(new ImportedSection(
                    section, CountItems(packFolder, section), reseeded.Contains(section)));
            }

            progress?.Report(new ImportProgress("done", bytesTotal, bytesTotal, 1, null));
            return new ImportResult(errors.Count == 0, sections, errors);
        }
        finally
        {
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch (Exception ex) { _log.LogWarning(ex, "Content import: could not delete temp folder {Dir}.", tempDir); }
            }
        }
    }

    private static bool IsZip(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static ContentManifest? ReadManifestFromFolder(string folder)
    {
        var p = Path.Combine(folder, "manifest.json");
        return File.Exists(p)
            ? JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(p), JsonOpts)
            : null;
    }

    private static ContentManifest? ReadManifestFromZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        // manifest.json at the archive root, or inside a single top-level folder.
        var entry = zip.GetEntry("manifest.json")
            ?? zip.Entries.FirstOrDefault(e =>
                e.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)
                && e.FullName.Count(c => c == '/') == 1);
        if (entry is null) return null;

        using var s = entry.Open();
        return JsonSerializer.Deserialize<ContentManifest>(s, JsonOpts);
    }

    /// <summary>
    /// After extraction the pack files may sit directly in <paramref name="extractedDir"/> or inside
    /// a single wrapping folder (e.g. "EnglishStudio-Content/"). Returns whichever holds manifest.json.
    /// </summary>
    private static string ResolvePackRoot(string extractedDir)
    {
        if (File.Exists(Path.Combine(extractedDir, "manifest.json"))) return extractedDir;

        var subdirs = Directory.GetDirectories(extractedDir);
        if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "manifest.json")))
            return subdirs[0];

        return extractedDir;
    }

    /// <summary>Key file/folder that proves a section is present in the pack (mirrors FileSystemContentStore).</summary>
    private static bool SectionKeyFileExists(string packFolder, ContentSection section) => section switch
    {
        ContentSection.DictionaryOxford => File.Exists(Path.Combine(packFolder, "Dictionary", "oxford_5000.json")),
        ContentSection.DictionaryPhave  => File.Exists(Path.Combine(packFolder, "Dictionary", "phave.json")),
        ContentSection.Reading          => File.Exists(Path.Combine(packFolder, "Reading", "ielts_reading_tests.json")),
        ContentSection.Listening        => File.Exists(Path.Combine(packFolder, "Listening", "ielts_listening_tests.json")),
        ContentSection.Writing          => File.Exists(Path.Combine(packFolder, "Writing", "writing_tests.json")),
        ContentSection.Speaking         => Directory.Exists(Path.Combine(packFolder, "Speaking"))
                                           && Directory.EnumerateFiles(Path.Combine(packFolder, "Speaking"), "*.txt", SearchOption.AllDirectories).Any(),
        ContentSection.Rubrics          => File.Exists(Path.Combine(packFolder, "Rubrics", "IeltsRubric_Writing.md"))
                                           && File.Exists(Path.Combine(packFolder, "Rubrics", "IeltsRubric_Speaking.md")),
        _ => false,
    };

    /// <summary>Best-effort item count for the summary; never throws.</summary>
    private static int CountItems(string packFolder, ContentSection section)
    {
        try
        {
            switch (section)
            {
                case ContentSection.Reading:   return CountTopArray(Path.Combine(packFolder, "Reading", "ielts_reading_tests.json"));
                case ContentSection.Listening: return CountTopArray(Path.Combine(packFolder, "Listening", "ielts_listening_tests.json"));
                case ContentSection.Writing:   return CountTopArray(Path.Combine(packFolder, "Writing", "writing_tests.json"));
                case ContentSection.DictionaryOxford: return CountTopArray(Path.Combine(packFolder, "Dictionary", "oxford_5000.json"));
                case ContentSection.DictionaryPhave:  return CountTopArray(Path.Combine(packFolder, "Dictionary", "phave.json"));
                case ContentSection.Speaking:
                    var dir = Path.Combine(packFolder, "Speaking");
                    return Directory.Exists(dir)
                        ? Directory.EnumerateFiles(dir, "*.txt", SearchOption.AllDirectories).Count()
                        : 0;
                case ContentSection.Rubrics:
                    var rdir = Path.Combine(packFolder, "Rubrics");
                    return Directory.Exists(rdir)
                        ? Directory.EnumerateFiles(rdir, "*.md").Count()
                        : 0;
                default: return 0;
            }
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Length of the JSON top-level array, or of the first array-valued property when the root is an
    /// object (Oxford/PHaVE wrap their entries in a single property). 0 otherwise / on error.
    /// </summary>
    private static int CountTopArray(string path)
    {
        if (!File.Exists(path)) return 0;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array) return root.GetArrayLength();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                    return prop.Value.GetArrayLength();
            }
        }
        return 0;
    }
}
