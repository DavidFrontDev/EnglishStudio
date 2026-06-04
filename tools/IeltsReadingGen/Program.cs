using System.Text.Json;
using EnglishStudio.IeltsReadingGen;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Ielts.Reading.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─── Configuration ────────────────────────────────────────────────────────────

const int MaxAttemptsPerTest = 3;
var outputDir = Path.Combine(AppContext.BaseDirectory, "Output");
Directory.CreateDirectory(outputDir);

// CLI args: optional list of codes to (re)generate, otherwise everything missing in Output/.
var explicitCodes = args.Where(a => a.StartsWith("acad-r-")).ToHashSet(StringComparer.OrdinalIgnoreCase);
var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase);
var aggregate = args.Contains("--aggregate", StringComparer.OrdinalIgnoreCase);

// ─── DI bootstrap ─────────────────────────────────────────────────────────────

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
services.AddAiModule();
var provider = services.BuildServiceProvider();

var log = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Gen");
var cli = provider.GetRequiredService<IClaudeCliClient>();

await cli.RefreshAsync();
if (!cli.IsAvailable)
{
    log.LogError("Claude CLI not found. Install it (or set PATH) and retry.");
    return 1;
}
log.LogInformation("Claude CLI: {Path}  ({Version})", cli.ExecutablePath, cli.Version);

// ─── Optionally just aggregate without generating ─────────────────────────────

if (aggregate)
{
    AggregateAndCopyToSeed(outputDir, log);
    return 0;
}

// ─── Pick which tests to generate ─────────────────────────────────────────────

var plans = Topics.All
    .Where(p => explicitCodes.Count == 0 || explicitCodes.Contains(p.Code))
    .ToList();

if (explicitCodes.Count == 0)
{
    // Skip those already present (resume-friendly).
    var present = Directory.GetFiles(outputDir, "acad-r-*.json")
        .Select(Path.GetFileNameWithoutExtension)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    plans = plans.Where(p => !present.Contains(p.Code)).ToList();
}

log.LogInformation("Planned: {N} test(s). Output → {Out}", plans.Count, outputDir);

if (dryRun)
{
    foreach (var p in plans) log.LogInformation("[dry-run] would generate {Code}", p.Code);
    return 0;
}

// ─── Generation loop ──────────────────────────────────────────────────────────

var produced = 0;
var failed = new List<string>();
var sw = System.Diagnostics.Stopwatch.StartNew();

foreach (var plan in plans)
{
    log.LogInformation("──── {Code} ───────────────────────────────────────────", plan.Code);
    log.LogInformation("Topics: P1={P1} | P2={P2} | P3={P3}", plan.P1Topic, plan.P2Topic, plan.P3Topic);

    var prompt = PromptBuilder.Build(plan);

    var success = false;
    for (var attempt = 1; attempt <= MaxAttemptsPerTest; attempt++)
    {
        log.LogInformation("Attempt {N}/{M} — calling Claude CLI…", attempt, MaxAttemptsPerTest);

        ClaudeCliResponse response;
        try
        {
            response = await cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromMinutes(8));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "CLI call failed");
            await Task.Delay(TimeSpan.FromSeconds(5));
            continue;
        }

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            log.LogWarning("CLI returned error or empty body.");
            continue;
        }

        // Extract JSON object from response text (some CLI responses wrap with prose).
        var jsonText = ExtractJsonObject(response.Text);
        if (jsonText is null)
        {
            log.LogWarning("No JSON object found in response. First 200 chars: {Sample}",
                response.Text[..Math.Min(response.Text.Length, 200)]);
            continue;
        }

        var validation = ReadingValidator.Validate(jsonText, plan.Code);
        if (!validation.Success)
        {
            log.LogWarning("Validation failed: {Err}", validation.Error);
            continue;
        }

        // Persist (pretty-printed for easier human review).
        var prettified = JsonSerializer.Serialize(validation.Dto, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        var outPath = Path.Combine(outputDir, plan.Code + ".json");
        await File.WriteAllTextAsync(outPath, prettified);

        var q = validation.Dto!.Parts.Sum(p => p.Groups?.Sum(g => g.Questions?.Count ?? 0) ?? 0);
        log.LogInformation("✓ {Code} saved ({Q} questions, {Bytes} bytes, {Ms}ms CLI)",
            plan.Code, q, prettified.Length, response.DurationMs);

        produced++;
        success = true;
        break;
    }

    if (!success)
    {
        log.LogError("✗ {Code} failed after {N} attempts.", plan.Code, MaxAttemptsPerTest);
        failed.Add(plan.Code);
    }

    // Light throttle between tests to avoid hammering rate limits.
    await Task.Delay(TimeSpan.FromSeconds(2));
}

sw.Stop();
log.LogInformation("════ Done in {Min} min. Generated {Ok}, failed {Fail}.", sw.Elapsed.TotalMinutes.ToString("F1"), produced, failed.Count);
if (failed.Count > 0)
{
    log.LogWarning("Failed codes: {List}", string.Join(", ", failed));
    log.LogWarning("Re-run with these codes as args to retry.");
}

// Auto-aggregate after successful run.
AggregateAndCopyToSeed(outputDir, log);

return failed.Count == 0 ? 0 : 2;

// ─── Helpers ──────────────────────────────────────────────────────────────────

static string? ExtractJsonObject(string raw)
{
    var trimmed = raw.Trim();
    // Strip ```json fences if present.
    if (trimmed.StartsWith("```"))
    {
        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline > 0) trimmed = trimmed[(firstNewline + 1)..];
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0) trimmed = trimmed[..lastFence];
    }
    var start = trimmed.IndexOf('{');
    var end = trimmed.LastIndexOf('}');
    return (start >= 0 && end > start) ? trimmed[start..(end + 1)] : null;
}

static void AggregateAndCopyToSeed(string outputDir, ILogger log)
{
    var files = Directory.GetFiles(outputDir, "acad-r-*.json").OrderBy(f => f).ToArray();
    if (files.Length == 0)
    {
        log.LogWarning("Aggregate: no Output/*.json files found.");
        return;
    }

    var aggregated = new List<JsonElement>();
    foreach (var file in files)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        aggregated.Add(doc.RootElement.Clone());
    }

    var seedPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "EnglishStudio.Modules.Ielts.Reading", "Seed", "ielts_reading_tests.json"));

    var json = JsonSerializer.Serialize(aggregated, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(seedPath, json);
    log.LogInformation("Aggregate: wrote {N} tests → {Path}", aggregated.Count, seedPath);
}
