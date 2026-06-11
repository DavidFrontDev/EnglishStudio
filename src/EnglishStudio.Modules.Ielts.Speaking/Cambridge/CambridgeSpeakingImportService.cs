using System.IO;
using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Speaking.Cambridge;

/// <summary>
/// Imports Speaking questions from the imported content-pack
/// (%AppData%\EnglishStudio\IeltsContent\Speaking\Ielts {15..20}\Test№{1..4}.txt).
/// Replaces any previous Speaking content the first time it runs; subsequent runs are
/// idempotent (banks identified by TopicCode "cambridge-{book}-test-{n}-part-{p}").
/// </summary>
public sealed class CambridgeSpeakingImportService
{
    public const string TopicCodePrefix = "cambridge-";
    private static readonly int[] Books = { 15, 16, 17, 18, 19, 20 };
    private static readonly int[] TestNumbers = { 1, 2, 3, 4 };

    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly CambridgeSpeakingTestParser _parser;
    private readonly ILogger<CambridgeSpeakingImportService> _log;
    private readonly string _baseFolder;

    public CambridgeSpeakingImportService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        CambridgeSpeakingTestParser parser,
        ILogger<CambridgeSpeakingImportService> log,
        string? baseFolder = null)
    {
        _dbFactory = dbFactory;
        _parser = parser;
        _log = log;
        _baseFolder = baseFolder
            ?? Path.Combine(DictionaryPaths.IeltsContentRoot, "Speaking");
    }

    public async Task ImportIfPossibleAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_baseFolder))
        {
            _log.LogInformation(
                "Cambridge Speaking import: base folder not present ({Folder}); skipping.",
                _baseFolder);
            return;
        }

        var parsed = ParseAvailableTests();
        if (parsed.Count == 0)
        {
            _log.LogInformation("Cambridge Speaking import: no .txt tests found under {Folder}.", _baseFolder);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // First run after the format switch: clear any pre-existing non-Cambridge banks
        // (legacy AI seed). Once we have at least one Cambridge bank in the DB this guard
        // becomes a no-op, so subsequent runs are idempotent.
        var hasCambridge = await db.SpeakingQuestionBanks
            .AnyAsync(b => b.TopicCode.StartsWith(TopicCodePrefix), ct);
        if (!hasCambridge)
        {
            try
            {
                var legacy = await db.SpeakingQuestionBanks
                    .Where(b => !b.TopicCode.StartsWith(TopicCodePrefix))
                    .ToListAsync(ct);
                if (legacy.Count > 0)
                {
                    // SpeakingResponses → SpeakingQuestions is Restrict, so responses recorded
                    // against legacy questions must go first or the bank delete fails.
                    var legacyResponses = await db.SpeakingResponses
                        .Where(r => !r.Question.Bank.TopicCode.StartsWith(TopicCodePrefix))
                        .ToListAsync(ct);
                    if (legacyResponses.Count > 0)
                        db.SpeakingResponses.RemoveRange(legacyResponses);

                    db.SpeakingQuestionBanks.RemoveRange(legacy);
                    await db.SaveChangesAsync(ct);
                    _log.LogInformation(
                        "Cambridge Speaking import: removed {Count} legacy bank(s) and {Responses} dependent response(s).",
                        legacy.Count, legacyResponses.Count);
                }
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                _log.LogWarning(ex, "Cambridge Speaking import: legacy bank cleanup failed; continuing with import.");
            }
        }

        var existingCodes = await db.SpeakingQuestionBanks
            .Select(b => b.TopicCode)
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        // First pass: insert banks (without Part 3 → Part 2 link).
        var insertedPart2 = new Dictionary<string, SpeakingQuestionBank>(StringComparer.OrdinalIgnoreCase);
        var pendingPart3Links = new List<(SpeakingQuestionBank P3, string Part2Code)>();
        var newBanks = new List<SpeakingQuestionBank>();

        foreach (var test in parsed)
        {
            var basePrefix = $"{TopicCodePrefix}{test.Book}-test-{test.TestNumber}";

            var p1Bank = BuildPart1Bank(test, basePrefix);
            if (p1Bank is not null && !existing.Contains(p1Bank.TopicCode))
            {
                db.SpeakingQuestionBanks.Add(p1Bank);
                newBanks.Add(p1Bank);
            }

            var p2Bank = BuildPart2Bank(test, basePrefix);
            if (p2Bank is not null && !existing.Contains(p2Bank.TopicCode))
            {
                db.SpeakingQuestionBanks.Add(p2Bank);
                newBanks.Add(p2Bank);
                insertedPart2[p2Bank.TopicCode] = p2Bank;
            }

            var p3Bank = BuildPart3Bank(test, basePrefix);
            if (p3Bank is not null && !existing.Contains(p3Bank.TopicCode))
            {
                db.SpeakingQuestionBanks.Add(p3Bank);
                newBanks.Add(p3Bank);
                pendingPart3Links.Add((p3Bank, $"{basePrefix}-part-2"));
            }
        }

        if (newBanks.Count == 0)
        {
            _log.LogDebug("Cambridge Speaking import: nothing new to insert.");
            return;
        }

        await db.SaveChangesAsync(ct);

        // Second pass: link Part 3 banks to their Part 2 banks once both have IDs.
        var part2Lookup = await db.SpeakingQuestionBanks
            .Where(b => b.Part == SpeakingBankPart.Part2 && b.TopicCode.StartsWith(TopicCodePrefix))
            .ToDictionaryAsync(b => b.TopicCode, b => b.Id, StringComparer.OrdinalIgnoreCase, ct);

        var linked = 0;
        foreach (var (p3, code) in pendingPart3Links)
        {
            if (part2Lookup.TryGetValue(code, out var p2Id))
            {
                p3.LinkedPart2BankId = p2Id;
                linked++;
            }
        }
        if (linked > 0) await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Cambridge Speaking import: inserted {Banks} bank(s) with {Questions} question(s), {Links} Part3→Part2 link(s).",
            newBanks.Count,
            newBanks.Sum(b => b.Questions.Count),
            linked);
    }

    private List<CambridgeSpeakingTest> ParseAvailableTests()
    {
        var results = new List<CambridgeSpeakingTest>();
        foreach (var book in Books)
        {
            // Pack layout: Speaking/Ielts {book}/Test№{t}.txt (the Speaking segment is _baseFolder).
            var bookDir = Path.Combine(_baseFolder, $"Ielts {book}");
            if (!Directory.Exists(bookDir)) continue;
            foreach (var t in TestNumbers)
            {
                var file = Path.Combine(bookDir, $"Test№{t}.txt");
                if (!File.Exists(file)) continue;
                try
                {
                    results.Add(_parser.Parse(book, t, file));
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to parse Cambridge speaking test {Book}/{Test}", book, t);
                }
            }
        }
        return results;
    }

    private static SpeakingQuestionBank? BuildPart1Bank(CambridgeSpeakingTest test, string basePrefix)
    {
        if (test.Part1.Questions.Count == 0) return null;
        var bank = new SpeakingQuestionBank
        {
            Part = SpeakingBankPart.Part1,
            TopicCode = $"{basePrefix}-part-1",
            TopicLabel = $"Cambridge {test.Book} Test {test.TestNumber} — Part 1: {test.Part1.TopicLabel}"
        };
        var order = 1;
        foreach (var q in test.Part1.Questions)
        {
            if (string.IsNullOrWhiteSpace(q)) continue;
            bank.Questions.Add(new SpeakingQuestion { OrderInBank = order++, Text = q });
        }
        return bank.Questions.Count > 0 ? bank : null;
    }

    private static SpeakingQuestionBank? BuildPart2Bank(CambridgeSpeakingTest test, string basePrefix)
    {
        if (string.IsNullOrWhiteSpace(test.Part2.CueCardPrompt)) return null;
        var bank = new SpeakingQuestionBank
        {
            Part = SpeakingBankPart.Part2,
            TopicCode = $"{basePrefix}-part-2",
            TopicLabel = $"Cambridge {test.Book} Test {test.TestNumber} — Part 2",
            CueCardPrompt = test.Part2.CueCardPrompt,
            CueCardSubpointsJson = test.Part2.Subpoints.Count > 0
                ? JsonSerializer.Serialize(test.Part2.Subpoints)
                : null
        };
        bank.Questions.Add(new SpeakingQuestion { OrderInBank = 1, Text = test.Part2.CueCardPrompt });
        return bank;
    }

    private static SpeakingQuestionBank? BuildPart3Bank(CambridgeSpeakingTest test, string basePrefix)
    {
        var flatQuestions = test.Part3.Subtopics.SelectMany(s => s.Questions).Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        if (flatQuestions.Count == 0) return null;
        var bank = new SpeakingQuestionBank
        {
            Part = SpeakingBankPart.Part3,
            TopicCode = $"{basePrefix}-part-3",
            TopicLabel = $"Cambridge {test.Book} Test {test.TestNumber} — Part 3"
        };
        var order = 1;
        foreach (var q in flatQuestions)
        {
            bank.Questions.Add(new SpeakingQuestion { OrderInBank = order++, Text = q });
        }
        return bank;
    }
}
