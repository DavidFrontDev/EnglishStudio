using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "external", "phave"));
var pdf = Path.Combine(root, "phave_main.pdf");
if (!File.Exists(pdf))
{
    Console.Error.WriteLine($"PDF not found at {pdf}");
    return 1;
}

Console.WriteLine($"Reading {pdf}");
var allText = new System.Text.StringBuilder();
using (var doc = PdfDocument.Open(pdf))
{
    Console.WriteLine($"Pages: {doc.NumberOfPages}");
    foreach (var page in doc.GetPages())
    {
        allText.Append(' ').Append(page.Text).Append(' ');
    }
}
var text = Regex.Replace(allText.ToString(), "\\s+", " ").Trim();

// Phrasal verb header: " N. PHRASE " where PHRASE is 2-3 uppercase tokens.
// Captures rank N and phrase. We then split the text into segments per phrasal.
var phraseRx = new Regex(
    @"(?:(?<=\s)|^)(?<rank>\d{1,3})\.\s+(?<phrase>[A-Z][A-Z]+(?:\s+[A-Z][A-Z']+){1,2})\s+(?=1\.)",
    RegexOptions.Compiled);

var headers = phraseRx.Matches(text);
Console.WriteLine($"Detected phrasal headers: {headers.Count}");

var senseRx = new Regex(
    @"(?<num>\d+)\.\s*(?:\((?<particle>[^)]+)\)\s+)?(?<def>.+?)\s+\((?<pct>[\d\.]+)\s*%\)\s+(?<example>.+?)(?=\s+\d+\.|\s*$)",
    RegexOptions.Compiled | RegexOptions.Singleline);

var phrasals = new List<PhrasalEntry>();
for (var i = 0; i < headers.Count; i++)
{
    var h = headers[i];
    var rank = int.Parse(h.Groups["rank"].Value, CultureInfo.InvariantCulture);
    var phrase = h.Groups["phrase"].Value.Trim();

    var blobStart = h.Index + h.Length;
    var blobEnd = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
    var blob = text.Substring(blobStart, blobEnd - blobStart).Trim();

    var senses = new List<PhrasalSense>();
    foreach (Match sm in senseRx.Matches(blob))
    {
        var senseNum = int.Parse(sm.Groups["num"].Value, CultureInfo.InvariantCulture);
        var particleHint = sm.Groups["particle"].Success ? sm.Groups["particle"].Value.Trim() : null;
        var def = sm.Groups["def"].Value.Trim();
        var pct = double.Parse(sm.Groups["pct"].Value, CultureInfo.InvariantCulture);
        var example = sm.Groups["example"].Value.Trim();
        senses.Add(new PhrasalSense(senseNum, particleHint, def, pct, example));
    }

    phrasals.Add(new PhrasalEntry(rank, phrase, senses));
}

Console.WriteLine($"Parsed phrasals: {phrasals.Count}");
Console.WriteLine($"Total senses:    {phrasals.Sum(p => p.Senses.Count)}");
var noSense = phrasals.Where(p => p.Senses.Count == 0).Select(p => p.Phrase).ToList();
if (noSense.Count > 0) Console.WriteLine($"WARN no senses: {string.Join(", ", noSense)}");

var seed = new PhaveSeedDocument(
    SchemaVersion: 1,
    SourceName: "PHaVE List (Garnier & Schmitt 2014)",
    SourceUrl: "https://www.norbertschmitt.co.uk/",
    GeneratedAt: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
    Entries: phrasals);

var outPath = Path.Combine(root, "phave_seed.json");
await using (var fs = File.Create(outPath))
{
    await JsonSerializer.SerializeAsync(fs, seed, new JsonSerializerOptions
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length} bytes)");

return 0;

internal record PhrasalSense(int Num, string? Particle, string DefinitionEn, double PercentOccurrence, string ExampleEn);
internal record PhrasalEntry(int Rank, string Phrase, List<PhrasalSense> Senses);
internal record PhaveSeedDocument(int SchemaVersion, string SourceName, string SourceUrl, string GeneratedAt, List<PhrasalEntry> Entries);
