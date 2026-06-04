using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Listening.Seed;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using EnglishStudio.Modules.Ielts.Writing.Seed;

// EnglishStudio content-pack builder.
//
// Assembles a distributable content-pack (folder + manifest.json) from the on-disk Seed/ assets in
// the repository. Mirrors what the old Copy*ToAppData/Extract* seed helpers used to do at runtime,
// but writes the canonical pack layout that ContentImportService now imports:
//
//   <out>/manifest.json
//   <out>/Dictionary/{oxford_5000.json, phave.json}
//   <out>/Reading/{ielts_reading_tests.json, <code>/<image>}
//   <out>/Listening/{ielts_listening_tests.json, <code>/{<audio>,<image>,transcript.txt}}
//   <out>/Writing/{writing_tests.json, <code>/<image>}
//   <out>/Speaking/Ielts {book}/Test№{t}.txt
//
// Usage:
//   ContentPackBuilder [--seed-root <repo/src>] [--out <folder>] [--speaking-src <telegram folder>]
//
// The output pack is private copyright content — distribute it to users separately; never commit it.

var opts = Args.Parse(args);

var seedRoot = opts.SeedRoot ?? Locate.SrcRoot();
if (seedRoot is null || !Directory.Exists(seedRoot))
{
    Console.Error.WriteLine(
        "ERROR: could not locate the repo 'src' folder. Pass --seed-root <path-to-repo>/src explicitly.");
    return 1;
}

var outDir = Path.GetFullPath(opts.Out ?? Path.Combine(Directory.GetCurrentDirectory(), "EnglishStudio-Content"));
Directory.CreateDirectory(outDir);

Console.WriteLine($"seed-root : {seedRoot}");
Console.WriteLine($"out       : {outDir}");
Console.WriteLine($"speaking  : {opts.SpeakingSrc ?? "(skipped — pass --speaking-src to include)"}");
Console.WriteLine();

var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var sections = new Dictionary<string, bool>();
var copied = new Counter();

// ── Dictionary (oxford_5000.json, phave.json) ──────────────────────────────────────────────
{
    var dictSeed = Path.Combine(seedRoot, "EnglishStudio.Modules.Dictionary", "Seed");
    var dictOut = Path.Combine(outDir, "Dictionary");
    var oxford = CopyIfExists(Path.Combine(dictSeed, "oxford_5000.json"), Path.Combine(dictOut, "oxford_5000.json"), copied);
    var phave = CopyIfExists(Path.Combine(dictSeed, "phave.json"), Path.Combine(dictOut, "phave.json"), copied);
    sections[ContentManifest.KeyOf(ContentSection.DictionaryOxford)] = oxford;
    sections[ContentManifest.KeyOf(ContentSection.DictionaryPhave)] = phave;
}

// ── Reading ─────────────────────────────────────────────────────────────────────────────────
{
    var readSeed = Path.Combine(seedRoot, "EnglishStudio.Modules.Ielts.Reading", "Seed");
    var json = Path.Combine(readSeed, "ielts_reading_tests.json");
    var any = false;
    if (File.Exists(json))
    {
        CopyIfExists(json, Path.Combine(outDir, "Reading", "ielts_reading_tests.json"), copied);
        any = true;

        var tests = JsonSerializer.Deserialize<List<ReadingTestDto>>(File.ReadAllText(json), jsonOpts) ?? new();
        foreach (var t in tests)
        {
            var rels = t.Parts
                .SelectMany(p => p.Groups)
                .Where(g => !string.IsNullOrWhiteSpace(g.ImagePath))
                .Select(g => g.ImagePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in rels)
                CopyMedia(Path.Combine(readSeed, "Images", $"{t.Code}.{rel}"),
                          Path.Combine(outDir, "Reading", t.Code, rel), t.Code, rel, copied);
        }
    }
    sections[ContentManifest.KeyOf(ContentSection.Reading)] = any;
}

// ── Listening ───────────────────────────────────────────────────────────────────────────────
{
    var lisSeed = Path.Combine(seedRoot, "EnglishStudio.Modules.Ielts.Listening", "Seed");
    var json = Path.Combine(lisSeed, "ielts_listening_tests.json");
    var any = false;
    if (File.Exists(json))
    {
        CopyIfExists(json, Path.Combine(outDir, "Listening", "ielts_listening_tests.json"), copied);
        any = true;

        var tests = JsonSerializer.Deserialize<List<ListeningTestDto>>(File.ReadAllText(json), jsonOpts) ?? new();
        foreach (var t in tests)
        {
            foreach (var audio in t.Parts.Where(p => !string.IsNullOrWhiteSpace(p.AudioFile)).Select(p => p.AudioFile!).Distinct(StringComparer.OrdinalIgnoreCase))
                CopyMedia(Path.Combine(lisSeed, "Audio", $"{t.Code}.{audio}"),
                          Path.Combine(outDir, "Listening", t.Code, audio), t.Code, audio, copied);

            foreach (var img in t.Parts.SelectMany(p => p.Groups).Where(g => !string.IsNullOrWhiteSpace(g.ImagePath)).Select(g => g.ImagePath!).Distinct(StringComparer.OrdinalIgnoreCase))
                CopyMedia(Path.Combine(lisSeed, "Images", $"{t.Code}.{img}"),
                          Path.Combine(outDir, "Listening", t.Code, img), t.Code, img, copied);

            CopyMedia(Path.Combine(lisSeed, "Transcripts", $"{t.Code}.transcript.txt"),
                      Path.Combine(outDir, "Listening", t.Code, "transcript.txt"), t.Code, "transcript.txt", copied, optional: true);
        }
    }
    sections[ContentManifest.KeyOf(ContentSection.Listening)] = any;
}

// ── Writing ─────────────────────────────────────────────────────────────────────────────────
{
    var wrSeed = Path.Combine(seedRoot, "EnglishStudio.Modules.Ielts.Writing", "Seed");
    var json = Path.Combine(wrSeed, "writing_tests.json");
    var any = false;
    if (File.Exists(json))
    {
        CopyIfExists(json, Path.Combine(outDir, "Writing", "writing_tests.json"), copied);
        any = true;

        var sets = JsonSerializer.Deserialize<List<WritingTestSetDto>>(File.ReadAllText(json), jsonOpts) ?? new();
        foreach (var s in sets)
        {
            foreach (var img in new[] { s.Task1.ImageFile, s.Task2.ImageFile }.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!).Distinct(StringComparer.OrdinalIgnoreCase))
                CopyMedia(Path.Combine(wrSeed, "Images", $"{s.Code}.{img}"),
                          Path.Combine(outDir, "Writing", s.Code, img), s.Code, img, copied);
        }
    }
    sections[ContentManifest.KeyOf(ContentSection.Writing)] = any;
}

// ── Speaking (from the local Telegram-style source) ────────────────────────────────────────
{
    var any = false;
    if (opts.SpeakingSrc is { } src && Directory.Exists(src))
    {
        int[] books = { 15, 16, 17, 18, 19, 20 };
        int[] tests = { 1, 2, 3, 4 };
        foreach (var book in books)
        {
            foreach (var t in tests)
            {
                var from = Path.Combine(src, $"Ielts {book}", "Speaking", $"Test№{t}", $"Test№{t}.txt");
                if (!File.Exists(from)) continue;
                CopyIfExists(from, Path.Combine(outDir, "Speaking", $"Ielts {book}", $"Test№{t}.txt"), copied);
                any = true;
            }
        }
        if (!any) Console.WriteLine("Speaking: no Test№*.txt found under the given --speaking-src.");
    }
    sections[ContentManifest.KeyOf(ContentSection.Speaking)] = any;
}

// ── Rubrics (AI band descriptors derived from IELTS public descriptors) ──────────────────────
{
    var aiRub = Path.Combine(seedRoot, "EnglishStudio.Modules.Ai", "Rubrics");
    var w = CopyIfExists(Path.Combine(aiRub, "IeltsRubric_Writing.md"), Path.Combine(outDir, "Rubrics", "IeltsRubric_Writing.md"), copied);
    var s = CopyIfExists(Path.Combine(aiRub, "IeltsRubric_Speaking.md"), Path.Combine(outDir, "Rubrics", "IeltsRubric_Speaking.md"), copied);
    sections[ContentManifest.KeyOf(ContentSection.Rubrics)] = w && s;
}

// ── manifest.json ───────────────────────────────────────────────────────────────────────────
var manifest = new ContentManifest
{
    PackVersion = 1,
    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
    Sections = sections,
};
File.WriteAllText(
    Path.Combine(outDir, "manifest.json"),
    JsonSerializer.Serialize(manifest, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    }));

Console.WriteLine();
Console.WriteLine($"Done. Copied {copied.Files} file(s), {copied.Bytes / 1024.0 / 1024.0:F1} MB.");
Console.WriteLine("Sections: " + string.Join(", ", sections.Where(kv => kv.Value).Select(kv => kv.Key)));
if (copied.Missing > 0)
    Console.WriteLine($"WARNING: {copied.Missing} referenced media file(s) were missing and skipped.");
return 0;

// ── helpers ───────────────────────────────────────────────────────────────────────────────────

static bool CopyIfExists(string from, string to, Counter c)
{
    if (!File.Exists(from)) return false;
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    File.Copy(from, to, overwrite: true);
    c.Add(new FileInfo(from).Length);
    return true;
}

static void CopyMedia(string from, string to, string code, string rel, Counter c, bool optional = false)
{
    if (CopyIfExists(from, to, c)) return;
    if (!optional)
    {
        c.Missing++;
        Console.WriteLine($"  missing: {code} → {rel}  (expected {from})");
    }
}

sealed class Counter
{
    public int Files;
    public long Bytes;
    public int Missing;
    public void Add(long bytes) { Files++; Bytes += bytes; }
}

sealed record Options(string? SeedRoot, string? Out, string? SpeakingSrc);

static class Args
{
    public static Options Parse(string[] args)
    {
        string? seedRoot = null, outDir = null, speaking = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--seed-root": seedRoot = args[++i]; break;
                case "--out": outDir = args[++i]; break;
                case "--speaking-src": speaking = args[++i]; break;
            }
        }
        return new Options(seedRoot, outDir, speaking);
    }
}

static class Locate
{
    /// <summary>Walks up from the running exe to the folder holding EnglishStudio.slnx, then returns its src/.</summary>
    public static string? SrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EnglishStudio.slnx")))
            {
                var src = Path.Combine(dir.FullName, "src");
                return Directory.Exists(src) ? src : null;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
