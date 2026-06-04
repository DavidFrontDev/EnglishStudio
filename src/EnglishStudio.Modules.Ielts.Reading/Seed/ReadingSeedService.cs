using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Reading.Seed;

public sealed class ReadingSeedService
{
    private const string ResourceFileName = "ielts_reading_tests.json";
    private const string ReadingContentFolder = "Reading";

    /// <summary>
    /// Codes of pilot/legacy reading tests that pre-dated the TestQuestionGroup schema.
    /// Removed at startup if they exist and have no user attempts to preserve.
    /// </summary>
    private static readonly string[] LegacyCodes = { "acad-r-001", "acad-r-006", "acad-r-test06", "pilot-academic-1" };

    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IContentStore _content;
    private readonly ILogger<ReadingSeedService> _log;

    public ReadingSeedService(IDbContextFactory<IeltsDbContext> dbFactory, IContentStore content, ILogger<ReadingSeedService> log)
    {
        _dbFactory = dbFactory;
        _content = content;
        _log = log;
    }

    /// <summary>
    /// Imports any imported Reading tests not yet present in the DB (matched by Code), reading the
    /// test JSON from %AppData%\EnglishStudio\IeltsContent\Reading\. Image assets already live under
    /// IeltsContent\Reading\&lt;code&gt;\ after a content-pack import, so no extraction is needed here.
    /// Soft no-op when the section has not been imported.
    /// </summary>
    public async Task SeedIfMissingAsync(CancellationToken ct = default)
    {
        if (!_content.IsImported(ContentSection.Reading))
        {
            _log.LogInformation("Reading: content not imported, skipping seed.");
            return;
        }

        var tests = LoadFromContent();
        if (tests.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await RemoveLegacyAsync(db, ct);

        var existing = await db.TestSets
            .Where(t => t.Section == IeltsSection.Reading)
            .Select(t => t.Code)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var toRefresh = new List<ReadingTestDto>();
        foreach (var dto in tests)
        {
            if (existingSet.Contains(dto.Code))
            {
                toRefresh.Add(dto);
                continue;
            }

            var entity = MapToEntity(dto);
            db.TestSets.Add(entity);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Reading seed: added {Added} new test(s).", added);
        }
        else
        {
            _log.LogDebug("Reading seed: all bundled tests already present.");
        }

        await RefreshContentAsync(db, toRefresh, ct);
    }

    // Bundled JSON evolves over time (typo fixes, NMTW corrections, AcceptableAnswers tweaks),
    // but existing user DBs would otherwise be frozen at first-seed content. Walk every still-bundled
    // test and update scalar fields on matched parts/groups/questions in place — TestAnswer rows are
    // FK-attached to TestQuestion.Id, so we never delete or re-create matched rows.
    private async Task RefreshContentAsync(IeltsDbContext db, List<ReadingTestDto> dtos, CancellationToken ct)
    {
        if (dtos.Count == 0) return;

        var codes = dtos.Select(d => d.Code).ToList();
        var entities = await db.TestSets
            .Where(t => t.Section == IeltsSection.Reading && codes.Contains(t.Code))
            .Include(t => t.Parts).ThenInclude(p => p.Groups).ThenInclude(g => g.Questions)
            .ToListAsync(ct);

        var byCode = entities.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            if (!byCode.TryGetValue(dto.Code, out var entity)) continue;

            // When the bundled test's shape changed — groups added/removed or moved between parts,
            // or a group's question count differs — in-place field updates can't reconcile it: the
            // loop below matches rows by order and never creates or moves them. Rebuild the whole
            // test from the DTO instead (in its own context so a failure can't corrupt this one),
            // then move on — in-place updates can't help a structurally-changed test either way.
            if (!SameStructure(entity, dto))
            {
                await TryRebuildAsync(entity.Id, dto, ct);
                continue;
            }

            entity.Title = dto.Title;
            entity.AuthorAttribution = dto.Attribution;
            entity.IsExamOnly = dto.IsExamOnly;

            foreach (var partDto in dto.Parts)
            {
                var part = entity.Parts.FirstOrDefault(p => p.OrderInTest == partDto.Order);
                if (part is null) continue; // structural change → leave for a manual migration

                part.Title = partDto.Title;
                part.BodyText = partDto.Body;
                part.IntroNoteRu = partDto.IntroNoteRu;

                foreach (var groupDto in partDto.Groups)
                {
                    var group = part.Groups.FirstOrDefault(g => g.OrderInPart == groupDto.Order);
                    if (group is null) continue;

                    var layout = Enum.TryParse<QuestionGroupLayout>(groupDto.Layout, ignoreCase: true, out var l)
                        ? l : QuestionGroupLayout.FlatList;
                    group.Layout = layout;
                    group.InstructionText = groupDto.Instruction;
                    group.SharedOptionsJson = groupDto.SharedOptions?.GetRawText();
                    group.SharedListTitle = groupDto.SharedListTitle;
                    group.ImagePath = string.IsNullOrWhiteSpace(groupDto.ImagePath)
                        ? null
                        : ResolveImageAbsolutePath(dto.Code, groupDto.ImagePath);
                    group.ExampleStem = groupDto.ExampleStem;
                    group.ExampleAnswer = groupDto.ExampleAnswer;
                    group.SummaryTemplate = groupDto.SummaryTemplate;

                    foreach (var qDto in groupDto.Questions)
                    {
                        var q = group.Questions.FirstOrDefault(x => x.OrderInPart == qDto.Order);
                        if (q is null) continue;

                        if (Enum.TryParse<QuestionType>(qDto.Type, ignoreCase: true, out var qt))
                            q.Type = qt;
                        q.Stem = qDto.Stem;
                        q.OptionsJson = qDto.Options.HasValue ? qDto.Options.Value.GetRawText() : null;
                        q.AnswerKeyJson = qDto.AnswerKey.GetRawText();
                        q.AcceptableAnswersJson = qDto.AcceptableAnswers.HasValue
                            ? qDto.AcceptableAnswers.Value.GetRawText()
                            : null;
                        q.Points = qDto.Points;
                        q.WordLimitMax = qDto.WordLimitMax;
                    }
                }
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            var updates = await db.SaveChangesAsync(ct);
            _log.LogInformation("Reading seed: refreshed bundled content ({Updates} field updates across {Tests} test(s)).",
                updates, dtos.Count);
        }
    }

    // Replaces a structurally-changed bundled test wholesale, in a dedicated DbContext so a partial
    // failure can't leave the caller's change tracker in a half-deleted state: drops the test's
    // attempts (cascading their answers — they're keyed to question rows whose identity changes on
    // rebuild), deletes the old test graph, then re-adds it from the DTO. Swallows any error so a
    // failed rebuild can never block startup seeding; the test is simply left as-is until next run.
    private async Task TryRebuildAsync(int testSetId, ReadingTestDto dto, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var entity = await db.TestSets
                .Include(t => t.Parts).ThenInclude(p => p.Groups).ThenInclude(g => g.Questions)
                .FirstOrDefaultAsync(t => t.Id == testSetId, ct);
            if (entity is null) return;

            var attempts = await db.TestAttempts.Where(a => a.TestSetId == testSetId).ToListAsync(ct);
            if (attempts.Count > 0)
            {
                db.TestAttempts.RemoveRange(attempts);    // cascades to TestAnswer rows
                await db.SaveChangesAsync(ct);
            }

            db.TestSets.Remove(entity);
            await db.SaveChangesAsync(ct);                // cascades parts → groups → questions
            db.TestSets.Add(MapToEntity(dto));
            await db.SaveChangesAsync(ct);

            _log.LogInformation(
                "Reading seed: rebuilt bundled test '{Code}' after a structural change (discarded {N} attempt(s)).",
                dto.Code, attempts.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Reading seed: could not rebuild structurally-changed test '{Code}'; left unchanged for this run.",
                dto.Code);
        }
    }

    // True when the DB entity and the bundled DTO share the same part/group/question shape
    // (equal counts, matched by order). A mismatch means rows would need to be created or moved
    // between parents, which the in-place refresh deliberately doesn't do.
    private static bool SameStructure(TestSet entity, ReadingTestDto dto)
    {
        if (entity.Parts.Count != dto.Parts.Count) return false;
        foreach (var partDto in dto.Parts)
        {
            var part = entity.Parts.FirstOrDefault(p => p.OrderInTest == partDto.Order);
            if (part is null || part.Groups.Count != partDto.Groups.Count) return false;

            foreach (var groupDto in partDto.Groups)
            {
                var group = part.Groups.FirstOrDefault(g => g.OrderInPart == groupDto.Order);
                if (group is null || group.Questions.Count != groupDto.Questions.Count) return false;
            }
        }
        return true;
    }

    private async Task RemoveLegacyAsync(IeltsDbContext db, CancellationToken ct)
    {
        var legacy = await db.TestSets
            .Where(t => t.Section == IeltsSection.Reading && LegacyCodes.Contains(t.Code))
            .ToListAsync(ct);

        foreach (var t in legacy)
        {
            var attemptCount = await db.TestAttempts.CountAsync(a => a.TestSetId == t.Id, ct);
            if (attemptCount > 0)
            {
                _log.LogInformation(
                    "Reading seed: legacy test '{Code}' has {N} user attempts — leaving it in place.",
                    t.Code, attemptCount);
                continue;
            }
            db.TestSets.Remove(t);
            _log.LogInformation("Reading seed: removed legacy pilot test '{Code}'.", t.Code);
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Resolves a group's relative <c>ImagePath</c> (e.g. "map.png") to an absolute path under
    /// <c>%AppData%\EnglishStudio\IeltsContent\Reading\&lt;test-code&gt;\</c>.
    /// </summary>
    public static string ResolveImageAbsolutePath(string testCode, string relativeImagePath)
    {
        return Path.Combine(GetTestContentRoot(testCode), relativeImagePath);
    }

    private static string GetTestContentRoot(string testCode)
    {
        return Path.Combine(DictionaryPaths.AppDataRoot, "IeltsContent", ReadingContentFolder, testCode);
    }

    private List<ReadingTestDto> LoadFromContent()
    {
        using var s = _content.OpenJson(ReadingContentFolder, ResourceFileName);
        if (s is null) return new List<ReadingTestDto>();

        var result = JsonSerializer.Deserialize<List<ReadingTestDto>>(s,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? new List<ReadingTestDto>();
    }

    private static TestSet MapToEntity(ReadingTestDto dto)
    {
        var mode = Enum.TryParse<IeltsTestMode>(dto.Mode, ignoreCase: true, out var m)
            ? m : IeltsTestMode.Academic;

        var testSet = new TestSet
        {
            Code = dto.Code,
            Title = dto.Title,
            Section = IeltsSection.Reading,
            Mode = mode,
            Source = ContentSource.Bundled,
            AuthorAttribution = dto.Attribution,
            IsExamOnly = dto.IsExamOnly,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var partDto in dto.Parts.OrderBy(p => p.Order))
        {
            var part = new TestPart
            {
                OrderInTest = partDto.Order,
                Title = partDto.Title,
                BodyText = partDto.Body,
                IntroNoteRu = partDto.IntroNoteRu
            };

            foreach (var groupDto in partDto.Groups.OrderBy(g => g.Order))
            {
                var layout = Enum.TryParse<QuestionGroupLayout>(groupDto.Layout, ignoreCase: true, out var l)
                    ? l : QuestionGroupLayout.FlatList;

                var group = new TestQuestionGroup
                {
                    OrderInPart = groupDto.Order,
                    Layout = layout,
                    InstructionText = groupDto.Instruction,
                    SharedOptionsJson = groupDto.SharedOptions?.GetRawText(),
                    SharedListTitle = groupDto.SharedListTitle,
                    ImagePath = string.IsNullOrWhiteSpace(groupDto.ImagePath)
                        ? null
                        : ResolveImageAbsolutePath(dto.Code, groupDto.ImagePath),
                    ExampleStem = groupDto.ExampleStem,
                    ExampleAnswer = groupDto.ExampleAnswer,
                    SummaryTemplate = groupDto.SummaryTemplate
                };

                foreach (var qDto in groupDto.Questions.OrderBy(q => q.Order))
                {
                    if (!Enum.TryParse<QuestionType>(qDto.Type, ignoreCase: true, out var qt))
                    {
                        throw new InvalidOperationException(
                            $"Unknown QuestionType '{qDto.Type}' in test '{dto.Code}', part {partDto.Order}, group {groupDto.Order}, q {qDto.Order}.");
                    }

                    var question = new TestQuestion
                    {
                        OrderInPart = qDto.Order,
                        Type = qt,
                        Stem = qDto.Stem,
                        OptionsJson = qDto.Options.HasValue ? qDto.Options.Value.GetRawText() : null,
                        AnswerKeyJson = qDto.AnswerKey.GetRawText(),
                        AcceptableAnswersJson = qDto.AcceptableAnswers.HasValue
                            ? qDto.AcceptableAnswers.Value.GetRawText()
                            : null,
                        Points = qDto.Points,
                        WordLimitMax = qDto.WordLimitMax,
                        Group = group
                    };
                    group.Questions.Add(question);
                    part.Questions.Add(question);
                }

                part.Groups.Add(group);
            }

            testSet.Parts.Add(part);
        }

        return testSet;
    }
}
