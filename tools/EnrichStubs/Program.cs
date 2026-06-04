using System.Net;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using Microsoft.EntityFrameworkCore;

DictionaryPaths.EnsureDirectoriesExist();

Console.OutputEncoding = System.Text.Encoding.UTF8;

var options = new DbContextOptionsBuilder<DictionaryDbContext>()
    .UseSqlite(DictionaryPaths.SqliteConnectionString)
    .Options;

await using var db = new DictionaryDbContext(options);

var stubs = await db.Words
    .Include(w => w.PartOfSpeech)
    .Where(w => (w.Source == WordSource.Awl || w.Source == WordSource.Avl)
                && !w.Senses.Any())
    .OrderBy(w => w.Headword)
    .ToListAsync();

Console.WriteLine($"Stubs to enrich: {stubs.Count}");
if (stubs.Count == 0) return 0;

using var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(15)
};
http.DefaultRequestHeaders.UserAgent.ParseAdd("EnglishStudio/1.0");

var enriched = 0;
var notFound = 0;
var failed = 0;
var idx = 0;

foreach (var w in stubs)
{
    idx++;
    var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(w.Lemma)}";
    HttpResponseMessage? resp = null;
    try
    {
        resp = await http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            notFound++;
            Console.WriteLine($"[{idx,3}/{stubs.Count}] {w.Headword,-20} 404 not found");
        }
        else
        {
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            using var jdoc = JsonDocument.Parse(body);
            var (senses, ipaUk, ipaUs) = ExtractFromApi(jdoc.RootElement, w.PartOfSpeech.Code);
            if (senses.Count == 0)
            {
                notFound++;
                Console.WriteLine($"[{idx,3}/{stubs.Count}] {w.Headword,-20} no matching POS");
            }
            else
            {
                var orderIdx = 0;
                foreach (var s in senses.Take(5))
                {
                    var sense = new Sense
                    {
                        WordId = w.Id,
                        DefinitionEn = s.Definition,
                        DefinitionRu = string.Empty,
                        OrderIndex = orderIdx++,
                    };
                    if (!string.IsNullOrWhiteSpace(s.Example))
                    {
                        sense.Examples.Add(new Example
                        {
                            WordId = w.Id,
                            TextEn = s.Example,
                            Source = "dictionaryapi.dev",
                        });
                    }
                    db.Senses.Add(sense);
                }
                if (w.IpaUk is null && ipaUk is not null) w.IpaUk = ipaUk;
                if (w.IpaUs is null && ipaUs is not null) w.IpaUs = ipaUs;
                enriched++;
                Console.WriteLine($"[{idx,3}/{stubs.Count}] {w.Headword,-20} +{senses.Count} senses");
            }
        }

        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"[{idx,3}/{stubs.Count}] {w.Headword,-20} FAIL: {ex.Message}");
    }
    finally
    {
        resp?.Dispose();
    }

    await Task.Delay(600); // ~1.7 req/s
}

Console.WriteLine();
Console.WriteLine($"Enriched : {enriched}");
Console.WriteLine($"NotFound : {notFound}");
Console.WriteLine($"Failed   : {failed}");
return 0;

static (List<(string Definition, string? Example)> Senses, string? IpaUk, string? IpaUs)
    ExtractFromApi(JsonElement root, string ourPosCode)
{
    var senses = new List<(string Definition, string? Example)>();
    string? ipaUk = null;
    string? ipaUs = null;

    if (root.ValueKind != JsonValueKind.Array) return (senses, ipaUk, ipaUs);

    foreach (var entry in root.EnumerateArray())
    {
        if (entry.TryGetProperty("phonetics", out var phs) && phs.ValueKind == JsonValueKind.Array)
        {
            foreach (var ph in phs.EnumerateArray())
            {
                if (!ph.TryGetProperty("text", out var txt) || txt.ValueKind != JsonValueKind.String) continue;
                var t = txt.GetString();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (ph.TryGetProperty("audio", out var au) && au.ValueKind == JsonValueKind.String)
                {
                    var aurl = au.GetString() ?? "";
                    if (aurl.Contains("-uk.mp3") && ipaUk is null) ipaUk = t;
                    else if (aurl.Contains("-us.mp3") && ipaUs is null) ipaUs = t;
                }
                if (ipaUk is null) ipaUk = t;
                if (ipaUs is null) ipaUs = t;
            }
        }

        if (entry.TryGetProperty("meanings", out var meanings) && meanings.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in meanings.EnumerateArray())
            {
                var pos = m.TryGetProperty("partOfSpeech", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString() ?? "" : "";
                var apiPosCode = MapApiPos(pos);
                if (ourPosCode != "other" && apiPosCode != ourPosCode) continue;

                if (!m.TryGetProperty("definitions", out var defs) || defs.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var d in defs.EnumerateArray())
                {
                    if (!d.TryGetProperty("definition", out var dd) || dd.ValueKind != JsonValueKind.String) continue;
                    var defText = dd.GetString();
                    if (string.IsNullOrWhiteSpace(defText)) continue;

                    string? ex = null;
                    if (d.TryGetProperty("example", out var exEl) && exEl.ValueKind == JsonValueKind.String)
                        ex = exEl.GetString();

                    senses.Add((defText!, ex));
                }
            }
        }
    }

    // если ничего не сматчили по POS — попробуем без фильтра по POS
    if (senses.Count == 0)
    {
        foreach (var entry in root.EnumerateArray())
        {
            if (!entry.TryGetProperty("meanings", out var meanings) || meanings.ValueKind != JsonValueKind.Array) continue;
            foreach (var m in meanings.EnumerateArray())
            {
                if (!m.TryGetProperty("definitions", out var defs) || defs.ValueKind != JsonValueKind.Array) continue;
                foreach (var d in defs.EnumerateArray())
                {
                    if (!d.TryGetProperty("definition", out var dd) || dd.ValueKind != JsonValueKind.String) continue;
                    var defText = dd.GetString();
                    if (string.IsNullOrWhiteSpace(defText)) continue;
                    string? ex = null;
                    if (d.TryGetProperty("example", out var exEl) && exEl.ValueKind == JsonValueKind.String)
                        ex = exEl.GetString();
                    senses.Add((defText!, ex));
                }
            }
        }
    }

    return (senses, ipaUk, ipaUs);
}

static string MapApiPos(string apiPos) => apiPos?.ToLowerInvariant() switch
{
    "noun" => "n",
    "verb" => "v",
    "adjective" => "adj",
    "adverb" => "adv",
    "preposition" => "prep",
    "pronoun" => "pron",
    "conjunction" => "conj",
    "determiner" => "det",
    "interjection" => "exclam",
    "exclamation" => "exclam",
    "numeral" => "num",
    "article" => "det",
    _ => "other",
};
