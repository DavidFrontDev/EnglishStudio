using System.Text;
using System.Text.Json;
using EnglishStudio.IeltsSpeakingBankGen;
using EnglishStudio.Modules.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── CLI parsing ───────────────────────────────────────────────────────────────
//   --output <path>     output speaking_bank.json (default: Seed/speaking_bank.json next to module)
//   --resume            don't regenerate topics already in the output file
//   --part1 / --part2 / --part3   restrict to one part (debug)
//   --dry-run           list batches that would run, generate nothing

string GetArg(string name, string? def = null)
{
    var idx = Array.FindIndex(args, a => a == name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : def ?? string.Empty;
}
bool HasFlag(string name) => args.Any(a => a == name);

var defaultOut = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..",
    "src", "EnglishStudio.Modules.Ielts.Speaking", "Seed", "speaking_bank.json"));
var outputPath = GetArg("--output", defaultOut);
var resume = HasFlag("--resume");
var dryRun = HasFlag("--dry-run");
var runPart1 = HasFlag("--part1") || !(HasFlag("--part2") || HasFlag("--part3"));
var runPart2 = HasFlag("--part2") || !(HasFlag("--part1") || HasFlag("--part3"));
var runPart3 = HasFlag("--part3") || !(HasFlag("--part1") || HasFlag("--part2"));
// If no part flag given, default is "all three" — the above logic produces that.
if (!HasFlag("--part1") && !HasFlag("--part2") && !HasFlag("--part3"))
{
    runPart1 = runPart2 = runPart3 = true;
}

// ── Services ──────────────────────────────────────────────────────────────────
using var sp = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }))
    .AddAiModule()
    .BuildServiceProvider();

var cli = sp.GetRequiredService<IClaudeCliClient>();
if (!cli.IsAvailable)
{
    Console.Error.WriteLine("Claude CLI not found in PATH. Install claude code first.");
    return 1;
}

var existing = LoadExisting(outputPath);
var existingCodes = existing.Select(e => e.TopicCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"Output: {outputPath}");
Console.WriteLine($"Existing entries: {existing.Count} ({existingCodes.Count} unique codes)");
Console.WriteLine($"Resume: {resume}");
Console.WriteLine($"Dry run: {dryRun}");
Console.WriteLine();

// ── Part 1: per-topic batches of 5 questions ──────────────────────────────────
if (runPart1)
{
    Console.WriteLine("=== Part 1 ===");
    foreach (var (code, label) in Topics.Part1)
    {
        if (resume && existingCodes.Contains(code))
        {
            Console.WriteLine($"  [skip] {code}");
            continue;
        }
        if (dryRun) { Console.WriteLine($"  [dry] {code} — would generate 5 Q"); continue; }

        var questions = await GeneratePart1Async(cli, label);
        if (questions.Count < 3) // pathological response
        {
            Console.Error.WriteLine($"  [WARN] {code}: only {questions.Count} q parsed; retrying once…");
            await Task.Delay(2000);
            questions = await GeneratePart1Async(cli, label);
        }
        var dto = new SpeakingBankDto
        {
            Part = "Part1",
            TopicCode = code,
            TopicLabel = label,
            Questions = questions
        };
        existing.Add(dto);
        existingCodes.Add(code);
        SaveAtomic(outputPath, existing);
        Console.WriteLine($"  [ok] {code}: {questions.Count} questions");
        await Task.Delay(1000);
    }
}

// ── Part 2: per-topic cue card ────────────────────────────────────────────────
if (runPart2)
{
    Console.WriteLine();
    Console.WriteLine("=== Part 2 ===");
    foreach (var (code, label, theme) in Topics.Part2)
    {
        var p2Code = $"p2-{code}";
        if (resume && existingCodes.Contains(p2Code))
        {
            Console.WriteLine($"  [skip] {p2Code}");
            continue;
        }
        if (dryRun) { Console.WriteLine($"  [dry] {p2Code} — would generate cue card"); continue; }

        var (prompt, subpoints) = await GeneratePart2Async(cli, label, theme);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            Console.Error.WriteLine($"  [WARN] {p2Code}: empty cue card; retrying…");
            await Task.Delay(2000);
            (prompt, subpoints) = await GeneratePart2Async(cli, label, theme);
        }
        var dto = new SpeakingBankDto
        {
            Part = "Part2",
            TopicCode = p2Code,
            TopicLabel = label,
            CueCardPrompt = prompt,
            CueCardSubpoints = subpoints
        };
        existing.Add(dto);
        existingCodes.Add(p2Code);
        SaveAtomic(outputPath, existing);
        Console.WriteLine($"  [ok] {p2Code}: {prompt.Length} chars, {subpoints.Count} subpoints");
        await Task.Delay(1000);
    }
}

// ── Part 3: per-topic 8 follow-ups ────────────────────────────────────────────
if (runPart3)
{
    Console.WriteLine();
    Console.WriteLine("=== Part 3 ===");
    foreach (var (code, label, theme) in Topics.Part2)
    {
        var p3Code = $"p3-{code}";
        if (resume && existingCodes.Contains(p3Code))
        {
            Console.WriteLine($"  [skip] {p3Code}");
            continue;
        }
        if (dryRun) { Console.WriteLine($"  [dry] {p3Code} — would generate 8 follow-ups"); continue; }

        var questions = await GeneratePart3Async(cli, label, theme);
        if (questions.Count < 5)
        {
            Console.Error.WriteLine($"  [WARN] {p3Code}: only {questions.Count} parsed; retrying…");
            await Task.Delay(2000);
            questions = await GeneratePart3Async(cli, label, theme);
        }
        var dto = new SpeakingBankDto
        {
            Part = "Part3",
            TopicCode = p3Code,
            TopicLabel = $"Discussion: {label}",
            LinkedPart2TopicCode = $"p2-{code}",
            Questions = questions
        };
        existing.Add(dto);
        existingCodes.Add(p3Code);
        SaveAtomic(outputPath, existing);
        Console.WriteLine($"  [ok] {p3Code}: {questions.Count} questions");
        await Task.Delay(1000);
    }
}

Console.WriteLine();
Console.WriteLine($"Done. Total entries: {existing.Count}");
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────

static async Task<List<string>> GeneratePart1Async(IClaudeCliClient cli, string topicLabel)
{
    var prompt = $$"""
        You are an IELTS Speaking examiner producing Part 1 questions.
        Topic: {{topicLabel}}.
        Write EXACTLY 5 short Part 1 questions in natural examiner English.
        Mix tenses: at least one present, one past, one general-truth or hypothetical.
        Each question 6-15 words, no double questions, no "describe" prompts (those are Part 2).
        Return ONLY a JSON object of the shape: {"questions": ["Q1", "Q2", ...]}
        No prose, no markdown fence.
        """;
    var resp = await cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromMinutes(2));
    return ParseQuestionList(resp.Text);
}

static async Task<(string Prompt, List<string> Subpoints)> GeneratePart2Async(
    IClaudeCliClient cli, string topicLabel, string theme)
{
    var prompt = $$"""
        You are an IELTS Speaking examiner writing a Part 2 cue card.
        Title: "{{topicLabel}}"
        Theme: {{theme}}
        Produce a complete IELTS cue card following the official format. The candidate has
        1 minute prep and speaks for 2 minutes.
        The cue card must contain:
          - The opening "Describe ..." sentence
          - "You should say:" header
          - 3-4 bullet sub-points (one short phrase each)
          - Closing "and explain ..." prompt
        Return ONLY a JSON object:
        {
          "prompt": "<the FULL cue card as a single string with \n line breaks>",
          "subpoints": ["sub1", "sub2", "sub3", "sub4"]
        }
        No prose, no fence.
        """;
    var resp = await cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromMinutes(2));
    var raw = ExtractJsonObject(resp.Text);
    if (raw is null) return (string.Empty, new());
    try
    {
        using var doc = JsonDocument.Parse(raw);
        var promptText = doc.RootElement.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
        var subs = new List<string>();
        if (doc.RootElement.TryGetProperty("subpoints", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) subs.Add(s.Trim());
            }
        }
        return (promptText.Trim(), subs);
    }
    catch (JsonException) { return (string.Empty, new()); }
}

static async Task<List<string>> GeneratePart3Async(IClaudeCliClient cli, string p2Label, string theme)
{
    var prompt = $$"""
        You are an IELTS Speaking examiner writing Part 3 follow-up questions for a Part 2 cue
        card that asked the candidate to describe {{theme}}.
        Part 2 cue card title: "{{p2Label}}".
        Write EXACTLY 8 Part 3 discussion questions that abstract from the personal anecdote to
        broader societal, comparative, or hypothetical angles. Order from easier (specific) to
        harder (abstract / opinion). Each question 8-22 words. Do NOT repeat the Part 2 prompt.
        Use "Do you think..." / "Why do some people..." / "How has X changed..." patterns.
        Return ONLY a JSON object: {"questions": ["Q1", ..., "Q8"]}
        No prose, no fence.
        """;
    var resp = await cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromMinutes(2));
    return ParseQuestionList(resp.Text);
}

static List<string> ParseQuestionList(string raw)
{
    var json = ExtractJsonObject(raw);
    if (json is null) return new();
    try
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("questions", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return new();
        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
        }
        return list;
    }
    catch (JsonException) { return new(); }
}

static string? ExtractJsonObject(string raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return null;
    var start = raw.IndexOf('{');
    var end = raw.LastIndexOf('}');
    return (start >= 0 && end > start) ? raw[start..(end + 1)] : null;
}

static List<SpeakingBankDto> LoadExisting(string path)
{
    if (!File.Exists(path)) return new();
    try
    {
        using var fs = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<SpeakingBankDto>>(fs,
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to load existing {path}: {ex.Message}");
        return new();
    }
}

static void SaveAtomic(string path, List<SpeakingBankDto> entries)
{
    var dir = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

    var opts = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    var tmp = path + ".tmp";
    File.WriteAllText(tmp, JsonSerializer.Serialize(entries, opts), new UTF8Encoding(false));
    File.Move(tmp, path, overwrite: true);
}

public sealed class SpeakingBankDto
{
    public string Part { get; set; } = string.Empty;
    public string TopicCode { get; set; } = string.Empty;
    public string TopicLabel { get; set; } = string.Empty;
    public string? CueCardPrompt { get; set; }
    public List<string>? CueCardSubpoints { get; set; }
    public string? LinkedPart2TopicCode { get; set; }
    public List<string>? Questions { get; set; }
}
