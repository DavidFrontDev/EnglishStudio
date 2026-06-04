using EnglishStudio.IeltsSpeakingAnswerSynth;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Speaking.Cambridge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

string GetArg(string name, string? def = null)
{
    var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : def ?? string.Empty;
}

bool HasFlag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

var import20 = HasFlag("--import-20");
var synthesize = HasFlag("--synthesize");
var audit = HasFlag("--audit") || (!import20 && !synthesize);
var dryRun = HasFlag("--dry-run");
var force = HasFlag("--force");
var minBook = int.TryParse(GetArg("--min-book", "15"), out var min) ? min : 15;
var maxBook = int.TryParse(GetArg("--max-book", "19"), out var max) ? max : 19;
var limit = int.TryParse(GetArg("--limit", ""), out var lim) ? lim : (int?)null;
var delayMs = int.TryParse(GetArg("--delay-ms", "2000"), out var delay) ? delay : 2000;
var maxRetries = int.TryParse(GetArg("--retries", "2"), out var retries) ? retries : 2;
var baseFolder = GetArg("--base", DefaultTelegramBase());
var answerPath = GetArg("--answer", FindBookFolder(baseFolder, 20) is { } b20
    ? Path.Combine(b20, "Speaking", "Answer", "Answer.txt")
    : string.Empty);
var filters = CollectFilters(args);

if (HasFlag("--help") || HasFlag("-h"))
{
    PrintUsage();
    return 0;
}

DictionaryPaths.EnsureDirectoriesExist();

using var sp = new ServiceCollection()
    .AddLogging(b => b.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    }).AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning))
    .AddAiModule()
    .AddIeltsCoreModule()
    .AddIeltsSpeakingModule()
    .BuildServiceProvider();

var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("SpeakingAnswerSynth");
log.LogInformation("IELTS DB: {Db}", DictionaryPaths.DatabaseFilePath);
log.LogInformation("Base folder: {Base}", baseFolder);

await using var scope = sp.CreateAsyncScope();
var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IeltsDbContext>>();
await using var db = await dbFactory.CreateDbContextAsync();
await db.Database.MigrateAsync();

var cli = scope.ServiceProvider.GetRequiredService<IClaudeCliClient>();
var synthesizer = new SpeakingAnswerSynthesizer(db, cli, log);

if (import20 || synthesize)
{
    var speakingImport = scope.ServiceProvider.GetRequiredService<CambridgeSpeakingImportService>();
    await speakingImport.ImportIfPossibleAsync();
}

if (audit)
{
    var result = await synthesizer.AuditAsync(CancellationToken.None);
    log.LogInformation("Audit: {Filled}/{Total} Cambridge speaking questions have ModelAnswer; missing {Missing}.",
        result.FilledModelAnswers, result.TotalCambridgeQuestions, result.MissingModelAnswers);
}

if (import20)
{
    if (!File.Exists(answerPath))
    {
        log.LogError("Answer file not found: {Path}", answerPath);
        return 1;
    }

    var tests = LoadTests(baseFolder, 20, log);
    if (tests.Count != 4)
    {
        log.LogError("Expected 4 Cambridge 20 speaking test files, found {Count}.", tests.Count);
        return 1;
    }

    var parser = new Cambridge20AnswerParser();
    var parsed = parser.Parse(answerPath, tests);
    log.LogInformation("Parsed Cambridge 20 answer pairs: {Count}", parsed.Count);
    await synthesizer.ImportCambridge20Async(parsed, dryRun, CancellationToken.None);
}

if (synthesize)
{
    await cli.RefreshAsync();
    if (!cli.IsAvailable)
    {
        log.LogError("Claude CLI not found. Install it or put it on PATH before running synthesis.");
        return 1;
    }

    log.LogInformation("Claude CLI: {Path} ({Version})", cli.ExecutablePath, cli.Version);
    var options = new SynthesisOptions(minBook, maxBook, filters, force, dryRun, limit, delayMs, maxRetries);
    return await synthesizer.SynthesizeAsync(options, CancellationToken.None);
}

return 0;

static List<string> CollectFilters(string[] args)
{
    var filters = new List<string>();
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], "--filter", StringComparison.OrdinalIgnoreCase)) continue;
        for (var j = i + 1; j < args.Length && !args[j].StartsWith("--"); j++)
        {
            filters.AddRange(args[j]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
    return filters;
}

static List<CambridgeSpeakingTest> LoadTests(string baseFolder, int book, ILogger log)
{
    var bookFolder = FindBookFolder(baseFolder, book);
    if (bookFolder is null)
    {
        log.LogError("Book folder not found for IELTS {Book} under {Base}", book, baseFolder);
        return new();
    }

    var parser = new CambridgeSpeakingTestParser();
    var tests = new List<CambridgeSpeakingTest>();
    for (var testNumber = 1; testNumber <= 4; testNumber++)
    {
        var testDir = Path.Combine(bookFolder, "Speaking", $"Test\u2116{testNumber}");
        var file = Path.Combine(testDir, $"Test\u2116{testNumber}.txt");
        if (!File.Exists(file))
        {
            log.LogWarning("Speaking test file not found: {File}", file);
            continue;
        }

        tests.Add(parser.Parse(book, testNumber, file));
    }

    return tests;
}

static string? FindBookFolder(string baseFolder, int book)
{
    var spaced = Path.Combine(baseFolder, $"Ielts {book}");
    if (Directory.Exists(spaced)) return spaced;

    var compact = Path.Combine(baseFolder, $"Ielts{book}");
    if (Directory.Exists(compact)) return compact;

    return null;
}

static string DefaultTelegramBase() =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads",
        "Telegram Desktop");

static void PrintUsage()
{
    Console.WriteLine("""
        Usage:
          dotnet run --project tools/IeltsSpeakingAnswerSynth -- --audit
          dotnet run --project tools/IeltsSpeakingAnswerSynth -- --import-20
          dotnet run --project tools/IeltsSpeakingAnswerSynth -- --synthesize

        Options:
          --dry-run              Do not write to DB or call synthesis writes.
          --force                Regenerate existing ModelAnswer values.
          --min-book 15          First Cambridge book for synthesis.
          --max-book 19          Last Cambridge book for synthesis.
          --filter <prefix>      TopicCode prefix filter. Repeatable or comma-separated.
          --limit <n>            Stop after n generated answers.
          --delay-ms 2000        Delay between Claude CLI calls.
          --retries 2            Validation retry count per answer.
          --base <path>          Downloads\Telegram Desktop folder.
          --answer <path>        Cambridge 20 Answer.txt path.
        """);
}
