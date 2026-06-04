using EnglishStudio.IeltsWritingBandGen;
using EnglishStudio.Modules.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ─── CLI parsing ──────────────────────────────────────────────────────────────
// Usage:
//   --seed <path>          Path to writing_tests.json (default: bundled seed)
//   --output <path>        Output dir (default: bin output/)
//   --code <code>          Filter to one task code (acad-w-test05-t2). Repeatable.
//   --bands 5,6,7,8,9      Filter to specific bands (default: all 5..9)
//   --audit                Only run audit + write gaps.json, no generation
//   --dry-run              List planned gaps, generate nothing
//   --merge                After generation (or alone), merge output/*.json into seed
//   --merge-only           Skip generation, just merge what is already in output/
//   --no-validate          Skip the validator step (faster, less accurate calibration)

string GetArg(string name, string? def = null)
{
    var idx = Array.FindIndex(args, a => a == name);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : def ?? string.Empty;
}
bool HasFlag(string name) => args.Any(a => a == name);

var seedPath = GetArg("--seed", DefaultSeedPath());
var outputDir = GetArg("--output", Path.Combine(AppContext.BaseDirectory, "output"));
// Collect every arg that follows a `--code` token: supports both
//   --code a b c          (space-separated list)
//   --code a --code b     (repeated flag form)
//   --code a,b,c          (comma-separated)
var codeFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < args.Length; i++)
{
    if (!string.Equals(args[i], "--code", StringComparison.OrdinalIgnoreCase)) continue;
    for (var j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++)
    {
        foreach (var token in args[j].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            codeFilters.Add(token);
        }
    }
}
var bandsFilter = (GetArg("--bands", "") is var bf && !string.IsNullOrWhiteSpace(bf))
    ? bf.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(int.Parse).ToHashSet()
    : new HashSet<int>(BandGapAuditor.RequiredBands);

var auditOnly = HasFlag("--audit");
var dryRun = HasFlag("--dry-run");
var doMerge = HasFlag("--merge");
var mergeOnly = HasFlag("--merge-only");
var noValidate = HasFlag("--no-validate");

Directory.CreateDirectory(outputDir);

// ─── DI bootstrap ─────────────────────────────────────────────────────────────

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
}));
services.AddAiModule();
var provider = services.BuildServiceProvider();

var log = provider.GetRequiredService<ILoggerFactory>().CreateLogger("BandGen");
log.LogInformation("Seed: {Seed}", seedPath);
log.LogInformation("Output: {Out}", outputDir);

// ─── Merge-only short-circuit ─────────────────────────────────────────────────

if (mergeOnly)
{
    return RunMerge(seedPath, outputDir, log);
}

// ─── Audit ────────────────────────────────────────────────────────────────────

var sets = BandGapAuditor.LoadSeed(seedPath);
var allGaps = BandGapAuditor.ComputeGaps(sets);

var filtered = allGaps
    .Where(g => codeFilters.Count == 0 || codeFilters.Contains(g.TaskCode))
    .Where(g => bandsFilter.Contains(g.TargetBand))
    .ToList();

log.LogInformation("Audit: {Total} total gaps in seed, {Filtered} after filters.",
    allGaps.Count, filtered.Count);

var gapsReportPath = Path.Combine(outputDir, "gaps.json");
BandGapAuditor.WriteGapsReport(filtered, gapsReportPath);
log.LogInformation("Wrote gap report → {Path}", gapsReportPath);

if (auditOnly)
{
    return 0;
}

if (dryRun)
{
    foreach (var g in filtered)
    {
        log.LogInformation("[dry-run] would generate {Code} band {Band}", g.TaskCode, g.TargetBand);
    }
    return 0;
}

// ─── Verify CLI ──────────────────────────────────────────────────────────────

var cli = provider.GetRequiredService<IClaudeCliClient>();
await cli.RefreshAsync();
if (!cli.IsAvailable)
{
    log.LogError("Claude CLI not found. Install it (or set PATH) and retry.");
    return 1;
}
log.LogInformation("Claude CLI: {Path} ({Version})", cli.ExecutablePath, cli.Version);

// ─── Run generator ────────────────────────────────────────────────────────────

var generator = new ClaudeBandGenerator(cli, log);
var validator = new ClaudeBandValidator(cli, log);
var orchestrator = new GenerationOrchestrator(generator, validator, log, outputDir, noValidate);
var failed = await orchestrator.RunAsync(filtered, CancellationToken.None);

// ─── Merge ────────────────────────────────────────────────────────────────────

if (doMerge)
{
    var rc = RunMerge(seedPath, outputDir, log);
    if (rc != 0) return rc;
}

return failed == 0 ? 0 : 2;

// ─── Helpers ──────────────────────────────────────────────────────────────────

static string DefaultSeedPath()
{
    return Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "EnglishStudio.Modules.Ielts.Writing", "Seed", "writing_tests.json"));
}

static int RunMerge(string seedPath, string outputDir, ILogger log)
{
    log.LogInformation("Merging output/*.json into {Seed}", seedPath);
    var result = SeedJsonMerger.Merge(seedPath, outputDir);
    log.LogInformation("Merge: added {Added}, skipped (already present) {Skip}, orphans {Orphan}.",
        result.Added, result.SkippedExisting, result.OrphanFiles);
    return 0;
}
