using System.Diagnostics;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using ICSharpCode.SharpZipLib.BZip2;
using Microsoft.EntityFrameworkCore;

const int MaxExamplesPerWord = 8;
const int MinSentenceLength = 25;     // chars
const int MaxSentenceLength = 180;

var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("EnglishStudio/1.0");

DictionaryPaths.EnsureDirectoriesExist();

var cacheDir = Path.Combine(DictionaryPaths.AppDataRoot, "_tatoeba_cache");
Directory.CreateDirectory(cacheDir);

var files = new (string Name, string Url)[]
{
    ("eng_sentences.tsv.bz2",  "https://downloads.tatoeba.org/exports/per_language/eng/eng_sentences.tsv.bz2"),
    ("rus_sentences.tsv.bz2",  "https://downloads.tatoeba.org/exports/per_language/rus/rus_sentences.tsv.bz2"),
    ("eng-rus_links.tsv.bz2",  "https://downloads.tatoeba.org/exports/per_language/eng/eng-rus_links.tsv.bz2"),
};

foreach (var (name, url) in files)
{
    var path = Path.Combine(cacheDir, name);
    if (File.Exists(path) && new FileInfo(path).Length > 0)
    {
        Console.WriteLine($"Already cached: {name} ({new FileInfo(path).Length:N0} bytes)");
        continue;
    }
    Console.WriteLine($"Downloading {url}...");
    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    resp.EnsureSuccessStatusCode();
    var tmp = path + ".tmp";
    await using (var fs = File.Create(tmp))
    await using (var src = await resp.Content.ReadAsStreamAsync())
        await src.CopyToAsync(fs);
    File.Move(tmp, path, overwrite: true);
    Console.WriteLine($"  → {new FileInfo(path).Length:N0} bytes");
}

var sw = Stopwatch.StartNew();
Console.WriteLine("Reading EN sentences...");
var engSentences = await ReadSentencesAsync(Path.Combine(cacheDir, "eng_sentences.tsv.bz2"));
Console.WriteLine($"  EN sentences: {engSentences.Count:N0}");

Console.WriteLine("Reading RU sentences...");
var rusSentences = await ReadSentencesAsync(Path.Combine(cacheDir, "rus_sentences.tsv.bz2"));
Console.WriteLine($"  RU sentences: {rusSentences.Count:N0}");

Console.WriteLine("Reading EN-RU links...");
var pairs = new List<(int EngId, int RusId)>();
using (var fs = File.OpenRead(Path.Combine(cacheDir, "eng-rus_links.tsv.bz2")))
using (var bz = new BZip2InputStream(fs))
using (var sr = new StreamReader(bz, System.Text.Encoding.UTF8))
{
    string? line;
    while ((line = await sr.ReadLineAsync()) != null)
    {
        var tab = line.IndexOf('\t');
        if (tab < 0) continue;
        if (!int.TryParse(line.AsSpan(0, tab), out var a)) continue;
        if (!int.TryParse(line.AsSpan(tab + 1), out var b)) continue;
        // some links are RU→EN; we want EN→RU. Test which side is in our maps.
        if (engSentences.ContainsKey(a) && rusSentences.ContainsKey(b))
            pairs.Add((a, b));
        else if (engSentences.ContainsKey(b) && rusSentences.ContainsKey(a))
            pairs.Add((b, a));
    }
}
Console.WriteLine($"  EN-RU pairs: {pairs.Count:N0}");

Console.WriteLine($"Read+parse done in {sw.Elapsed.TotalSeconds:F1}s. Filtering by headword...");

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;
await using var db = new DictionaryDbContext(options);

var allWords = await db.Words
    .Select(w => new { w.Id, w.Lemma, w.Headword })
    .ToListAsync();
var byLemma = allWords
    .GroupBy(w => w.Lemma, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList(), StringComparer.OrdinalIgnoreCase);
Console.WriteLine($"DB headwords: {byLemma.Count:N0}");

// Existing Tatoeba examples — для idempotency
var existingTatoeba = await db.Examples
    .Where(e => e.Source == "tatoeba")
    .Select(e => new { e.WordId, e.TextEn })
    .ToListAsync();
var existingSet = new HashSet<(int, string)>(
    existingTatoeba.Where(x => x.WordId.HasValue)
        .Select(x => (x.WordId!.Value, x.TextEn)));
var perWordCount = existingTatoeba
    .Where(x => x.WordId.HasValue)
    .GroupBy(x => x.WordId!.Value)
    .ToDictionary(g => g.Key, g => g.Count());

var inserted = 0;
var wordsWithExamples = 0;
var sw2 = Stopwatch.StartNew();

// Tokenize each EN sentence: extract lowercase words
foreach (var (engId, rusId) in pairs)
{
    var en = engSentences[engId];
    if (en.Length < MinSentenceLength || en.Length > MaxSentenceLength) continue;
    var ru = rusSentences[rusId];
    if (ru.Length < MinSentenceLength || ru.Length > MaxSentenceLength) continue;

    foreach (var token in Tokenize(en))
    {
        if (!byLemma.TryGetValue(token, out var wordIds)) continue;
        foreach (var wordId in wordIds)
        {
            perWordCount.TryGetValue(wordId, out var count);
            if (count >= MaxExamplesPerWord) continue;
            if (!existingSet.Add((wordId, en))) continue;

            db.Examples.Add(new Example
            {
                WordId = wordId,
                TextEn = en,
                TextRu = ru,
                Source = "tatoeba",
            });
            inserted++;
            perWordCount[wordId] = count + 1;
            if (count == 0) wordsWithExamples++;
        }
    }

    if (inserted > 0 && inserted % 2000 == 0)
    {
        await db.SaveChangesAsync();
        Console.WriteLine($"  inserted {inserted:N0} so far... ({sw2.Elapsed.TotalSeconds:F1}s)");
    }
}

await db.SaveChangesAsync();
Console.WriteLine();
Console.WriteLine($"Tatoeba import done in {sw2.Elapsed.TotalSeconds:F1}s.");
Console.WriteLine($"  Examples inserted     : {inserted:N0}");
Console.WriteLine($"  Distinct words touched: {wordsWithExamples:N0}");

return 0;

static async Task<Dictionary<int, string>> ReadSentencesAsync(string bz2Path)
{
    var dict = new Dictionary<int, string>();
    using var fs = File.OpenRead(bz2Path);
    using var bz = new BZip2InputStream(fs);
    using var sr = new StreamReader(bz, System.Text.Encoding.UTF8);
    string? line;
    while ((line = await sr.ReadLineAsync()) != null)
    {
        // format: id\tlang\ttext  (or sometimes id\ttext for per-lang files? always 3 cols)
        var firstTab = line.IndexOf('\t');
        if (firstTab < 0) continue;
        if (!int.TryParse(line.AsSpan(0, firstTab), out var id)) continue;
        var secondTab = line.IndexOf('\t', firstTab + 1);
        if (secondTab < 0) continue;
        dict[id] = line.Substring(secondTab + 1);
    }
    return dict;
}

static IEnumerable<string> Tokenize(string text)
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sb = new System.Text.StringBuilder();
    for (var i = 0; i <= text.Length; i++)
    {
        var c = i < text.Length ? text[i] : ' ';
        if (char.IsLetter(c) || c == '\'' || c == '-')
        {
            sb.Append(char.ToLowerInvariant(c));
        }
        else
        {
            if (sb.Length > 0)
            {
                var token = sb.ToString().TrimEnd('-', '\'');
                if (token.Length > 1 && seen.Add(token)) yield return token;
                sb.Clear();
            }
        }
    }
}
