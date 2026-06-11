using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Writing.Seed;

public sealed class WritingSeedService
{
    private const string ResourceFileName = "writing_tests.json";
    private const string WritingContentFolder = "Writing";

    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IContentStore _content;
    private readonly ILogger<WritingSeedService> _log;

    public WritingSeedService(IDbContextFactory<IeltsDbContext> dbFactory, IContentStore content, ILogger<WritingSeedService> log)
    {
        _dbFactory = dbFactory;
        _content = content;
        _log = log;
    }

    /// <summary>
    /// Imports any imported Writing test sets (TestSet + 2 WritingTask + model answers) not yet
    /// present in the DB, matched by <see cref="TestSet.Code"/>, reading from
    /// %AppData%\EnglishStudio\IeltsContent\Writing\. Task images already live under
    /// IeltsContent\Writing\&lt;code&gt;\ after a content-pack import. Soft no-op when not imported.
    /// </summary>
    public async Task SeedIfMissingAsync(CancellationToken ct = default)
    {
        if (!_content.IsImported(ContentSection.Writing))
        {
            _log.LogInformation("Writing: content not imported, skipping seed.");
            return;
        }

        var sets = LoadFromContent();
        if (sets.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.TestSets
            .Where(t => t.Section == IeltsSection.Writing)
            .ToDictionaryAsync(t => t.Code, t => t, StringComparer.OrdinalIgnoreCase, ct);

        // Tasks (with their ModelAnswers) for existing sets — we only need these when refreshing
        // bundled metadata, so loading them lazily avoids the cost on first-run / cold seed.
        Dictionary<string, WritingTask>? taskIndex = null;

        var added = 0;
        var updated = 0;
        var modelAnswersAdded = 0;
        foreach (var dto in sets)
        {
            if (existing.TryGetValue(dto.Code, out var current))
            {
                // Bundled metadata is the source of truth for Title/Attribution — refresh on every run
                // so renames in the JSON propagate to already-seeded databases without needing a reset.
                if (current.Title != dto.Title || current.AuthorAttribution != dto.Attribution)
                {
                    current.Title = dto.Title;
                    current.AuthorAttribution = dto.Attribution;
                    updated++;
                }

                // Upsert ModelAnswers: bundled JSON may grow new band-level reference essays for
                // a task that already exists in the DB (e.g. band-5/6/7/9 calibration anchors added
                // after the initial seed only had band 8). We INSERT missing bands but never modify
                // or remove existing reference essays — they may carry user-edited annotations.
                taskIndex ??= await LoadTaskIndexAsync(db, ct);
                modelAnswersAdded += UpsertModelAnswers(taskIndex, dto.Task1);
                modelAnswersAdded += UpsertModelAnswers(taskIndex, dto.Task2);
                continue;
            }

            var (testSet, task1, task2) = MapSet(dto);
            db.TestSets.Add(testSet);
            db.WritingTasks.Add(task1);
            db.WritingTasks.Add(task2);
            added++;
        }

        if (added > 0 || updated > 0 || modelAnswersAdded > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation(
                "Writing seed: added {Added} new set(s), refreshed metadata on {Updated} set(s), added {Refs} new model answer reference(s).",
                added, updated, modelAnswersAdded);
        }
        else
        {
            _log.LogDebug("Writing seed: all bundled test sets already present and up-to-date.");
        }
    }

    private static async Task<Dictionary<string, WritingTask>> LoadTaskIndexAsync(IeltsDbContext db, CancellationToken ct)
    {
        var tasks = await db.WritingTasks
            .Include(t => t.ModelAnswers)
            .ToListAsync(ct);
        return tasks.ToDictionary(t => t.Code, t => t, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For an already-seeded WritingTask, inserts any bundled ModelAnswer whose BandLevel is not
    /// yet present in the DB. Respects the unique (WritingTaskId, BandLevel) invariant by
    /// deduplicating on BandLevel; the first bundled answer for a band wins if the JSON
    /// accidentally contains duplicates. Returns the count of inserts.
    /// </summary>
    private static int UpsertModelAnswers(Dictionary<string, WritingTask> taskIndex, WritingTaskDto dto)
    {
        if (!taskIndex.TryGetValue(dto.Code, out var task)) return 0;

        var existingBands = task.ModelAnswers.Select(ma => ma.BandLevel).ToHashSet();
        var added = 0;
        foreach (var ma in dto.ModelAnswers)
        {
            if (ma.BandLevel <= 0) continue;          // placeholder rows in the seed JSON
            if (!existingBands.Add(ma.BandLevel)) continue; // already present

            task.ModelAnswers.Add(new WritingModelAnswer
            {
                BandLevel = ma.BandLevel,
                AnswerText = ma.AnswerText,
                AnnotationJson = ma.AnnotationJson,
                ExaminerComment = ma.ExaminerComment
            });
            added++;
        }
        return added;
    }

    private static (TestSet, WritingTask, WritingTask) MapSet(WritingTestSetDto dto)
    {
        var now = DateTime.UtcNow;

        var testSet = new TestSet
        {
            Code = dto.Code,
            Title = dto.Title,
            Section = IeltsSection.Writing,
            Mode = IeltsTestMode.Academic,
            Source = ContentSource.Bundled,
            AuthorAttribution = dto.Attribution,
            CreatedAt = now
        };

        var task1 = MapTask(dto.Task1, dto.Code, orderInSet: 1, now);
        var task2 = MapTask(dto.Task2, dto.Code, orderInSet: 2, now);

        // Both tasks will be linked through the TestSet navigation property
        task1.TestSet = testSet;
        task2.TestSet = testSet;

        return (testSet, task1, task2);
    }

    private static WritingTask MapTask(WritingTaskDto dto, string setCode, int orderInSet, DateTime now)
    {
        var kind = Enum.TryParse<WritingTaskKind>(dto.Kind, ignoreCase: true, out var k)
            ? k : (orderInSet == 2 ? WritingTaskKind.Task2 : WritingTaskKind.Task1Academic);
        var chartType = Enum.TryParse<WritingChartType>(dto.ChartType, ignoreCase: true, out var c)
            ? c : WritingChartType.None;

        var task = new WritingTask
        {
            Code = dto.Code,
            Kind = kind,
            PromptText = dto.PromptText,
            ChartSpecJson = dto.ChartSpecJson,
            ImagePath = string.IsNullOrWhiteSpace(dto.ImageFile)
                ? null
                : ResolveImageAbsolutePath(setCode, dto.ImageFile),
            MinWords = dto.MinWords > 0 ? dto.MinWords : DefaultMinWords(kind),
            RecommendedMinutes = dto.RecommendedMinutes > 0 ? dto.RecommendedMinutes : DefaultMinutes(kind),
            ChartType = chartType,
            TopicCategory = dto.TopicCategory,
            Source = ContentSource.Bundled,
            CreatedAt = now,
            OrderInSet = orderInSet
        };

        var seenBands = new HashSet<int>();
        foreach (var ma in dto.ModelAnswers)
        {
            if (ma.BandLevel <= 0) continue;
            if (!seenBands.Add(ma.BandLevel)) continue;

            task.ModelAnswers.Add(new WritingModelAnswer
            {
                BandLevel = ma.BandLevel,
                AnswerText = ma.AnswerText,
                AnnotationJson = ma.AnnotationJson,
                ExaminerComment = ma.ExaminerComment
            });
        }

        return task;
    }

    /// <summary>
    /// Resolves a task's relative image filename to an absolute path under
    /// %AppData%\EnglishStudio\IeltsContent\Writing\&lt;test-code&gt;\&lt;file&gt;.
    /// </summary>
    public static string ResolveImageAbsolutePath(string setCode, string imageFile)
    {
        return Path.Combine(GetContentRoot(setCode), imageFile);
    }

    private static string GetContentRoot(string setCode)
    {
        return Path.Combine(DictionaryPaths.AppDataRoot, "IeltsContent", WritingContentFolder, setCode);
    }

    private List<WritingTestSetDto> LoadFromContent()
    {
        using var s = _content.OpenJson(WritingContentFolder, ResourceFileName);
        if (s is null) return new List<WritingTestSetDto>();

        var result = JsonSerializer.Deserialize<List<WritingTestSetDto>>(s,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? new List<WritingTestSetDto>();
    }

    private static int DefaultMinWords(WritingTaskKind kind) =>
        kind == WritingTaskKind.Task2 ? 250 : 150;

    private static int DefaultMinutes(WritingTaskKind kind) =>
        kind == WritingTaskKind.Task2 ? 40 : 20;
}
