using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Listening.Seed;

public sealed class ListeningSeedService
{
    private const string ResourceFileName = "ielts_listening_tests.json";
    private const string ListeningContentFolder = "Listening";

    private static readonly Regex PartMarker = new(@"(?m)^PART\s+(\d+).*$", RegexOptions.Compiled);

    /// <summary>Codes of removed/superseded listening tests, deleted at startup if they have no user attempts.</summary>
    private static readonly string[] LegacyCodes = { "ielts16-test2-flowchart" };

    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IContentStore _content;
    private readonly ILogger<ListeningSeedService> _log;

    public ListeningSeedService(IDbContextFactory<IeltsDbContext> dbFactory, IContentStore content, ILogger<ListeningSeedService> log)
    {
        _dbFactory = dbFactory;
        _content = content;
        _log = log;
    }

    /// <summary>
    /// Imports any imported Listening tests not yet present in the DB (matched by Code), reading the
    /// test JSON, transcripts, audio and images from %AppData%\EnglishStudio\IeltsContent\Listening\
    /// (populated by a content-pack import). Soft no-op when the section has not been imported.
    /// </summary>
    public async Task SeedIfMissingAsync(CancellationToken ct = default)
    {
        if (!_content.IsImported(ContentSection.Listening))
        {
            _log.LogInformation("Listening: content not imported, skipping seed.");
            return;
        }

        var tests = LoadFromContent();
        if (tests.Count == 0) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await RemoveLegacyAsync(db, ct);

        var existing = await db.TestSets
            .Where(t => t.Section == IeltsSection.Listening)
            .Select(t => t.Code)
            .ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var toRefresh = new List<ListeningTestDto>();
        foreach (var dto in tests)
        {
            if (existingSet.Contains(dto.Code))
            {
                toRefresh.Add(dto);
                continue;
            }

            db.TestSets.Add(MapToEntity(dto, LoadTranscript(dto.Code)));
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("Listening seed: added {Added} new test(s).", added);
        }

        await RefreshContentAsync(db, toRefresh, ct);
    }

    private async Task RefreshContentAsync(IeltsDbContext db, List<ListeningTestDto> dtos, CancellationToken ct)
    {
        if (dtos.Count == 0) return;

        var codes = dtos.Select(d => d.Code).ToList();
        var entities = await db.TestSets
            .Where(t => t.Section == IeltsSection.Listening && codes.Contains(t.Code))
            .Include(t => t.Parts).ThenInclude(p => p.Groups).ThenInclude(g => g.Questions)
            .ToListAsync(ct);

        var byCode = entities.ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);

        foreach (var dto in dtos)
        {
            if (!byCode.TryGetValue(dto.Code, out var entity)) continue;

            entity.Title = dto.Title;
            entity.AuthorAttribution = dto.Attribution;
            entity.IsExamOnly = dto.IsExamOnly;

            var transcripts = LoadTranscript(dto.Code);
            foreach (var partDto in dto.Parts)
            {
                var part = entity.Parts.FirstOrDefault(p => p.OrderInTest == partDto.Order);
                if (part is null) continue;

                part.Title = partDto.Title;
                part.IntroNoteRu = partDto.IntroNoteRu;
                part.Transcript = transcripts.GetValueOrDefault(partDto.Order);
                part.AudioPath = string.IsNullOrWhiteSpace(partDto.AudioFile)
                    ? null
                    : ResolveAssetAbsolutePath(dto.Code, partDto.AudioFile);

                foreach (var groupDto in partDto.Groups)
                {
                    var group = part.Groups.FirstOrDefault(g => g.OrderInPart == groupDto.Order);
                    if (group is null) continue;

                    ApplyGroup(group, dto.Code, groupDto);

                    foreach (var qDto in groupDto.Questions)
                    {
                        var q = group.Questions.FirstOrDefault(x => x.OrderInPart == qDto.Order);
                        if (q is null) continue;
                        ApplyQuestion(q, qDto);
                    }
                }
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            var updates = await db.SaveChangesAsync(ct);
            _log.LogInformation("Listening seed: refreshed bundled content ({Updates} field updates across {Tests} test(s)).",
                updates, dtos.Count);
        }
    }

    private async Task RemoveLegacyAsync(IeltsDbContext db, CancellationToken ct)
    {
        var legacy = await db.TestSets
            .Where(t => t.Section == IeltsSection.Listening && LegacyCodes.Contains(t.Code))
            .ToListAsync(ct);
        if (legacy.Count == 0) return;

        var ids = legacy.Select(t => t.Id).ToList();

        // Legacy entries are throwaway (e.g. the Cascade preview) — drop them unconditionally,
        // including any attempts the user made while previewing, so they don't linger in the hub.
        await db.TestAttempts.Where(a => ids.Contains(a.TestSetId)).ExecuteDeleteAsync(ct);

        db.TestSets.RemoveRange(legacy);
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Listening seed: removed {N} legacy test(s): {Codes}.", legacy.Count, string.Join(", ", legacy.Select(t => t.Code)));
    }

    public static string ResolveAssetAbsolutePath(string testCode, string relativePath)
        => Path.Combine(GetTestContentRoot(testCode), relativePath);

    private static string GetTestContentRoot(string testCode)
        => Path.Combine(DictionaryPaths.AppDataRoot, "IeltsContent", ListeningContentFolder, testCode);

    /// <summary>
    /// Loads the imported transcript for a test from IeltsContent\Listening\&lt;code&gt;\transcript.txt
    /// and splits it by "PART n" markers into a per-part-order dictionary. Returns empty if absent.
    /// </summary>
    private IReadOnlyDictionary<int, string> LoadTranscript(string code)
    {
        var path = _content.ResolveFile(ListeningContentFolder, code, "transcript.txt");
        if (path is null) return new Dictionary<int, string>();

        var text = File.ReadAllText(path).Replace("\r\n", "\n");

        var result = new Dictionary<int, string>();
        var matches = PartMarker.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            if (!int.TryParse(matches[i].Groups[1].Value, out var num)) continue;
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            result[num] = text.Substring(start, end - start).Trim();
        }
        return result;
    }

    private List<ListeningTestDto> LoadFromContent()
    {
        using var s = _content.OpenJson(ListeningContentFolder, ResourceFileName);
        if (s is null) return new List<ListeningTestDto>();

        var result = JsonSerializer.Deserialize<List<ListeningTestDto>>(s,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? new List<ListeningTestDto>();
    }

    private static TestSet MapToEntity(ListeningTestDto dto, IReadOnlyDictionary<int, string> transcripts)
    {
        var testSet = new TestSet
        {
            Code = dto.Code,
            Title = dto.Title,
            Section = IeltsSection.Listening,
            Mode = IeltsTestMode.Academic,
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
                IntroNoteRu = partDto.IntroNoteRu,
                Transcript = transcripts.GetValueOrDefault(partDto.Order),
                AudioPath = string.IsNullOrWhiteSpace(partDto.AudioFile)
                    ? null
                    : ResolveAssetAbsolutePath(dto.Code, partDto.AudioFile)
            };

            foreach (var groupDto in partDto.Groups.OrderBy(g => g.Order))
            {
                var group = new TestQuestionGroup { OrderInPart = groupDto.Order };
                ApplyGroup(group, dto.Code, groupDto);

                foreach (var qDto in groupDto.Questions.OrderBy(q => q.Order))
                {
                    var question = new TestQuestion { OrderInPart = qDto.Order, Group = group };
                    ApplyQuestion(question, qDto);
                    group.Questions.Add(question);
                    part.Questions.Add(question);
                }

                part.Groups.Add(group);
            }

            testSet.Parts.Add(part);
        }

        return testSet;
    }

    private static void ApplyGroup(TestQuestionGroup group, string code, ListeningGroupDto dto)
    {
        group.Layout = Enum.TryParse<QuestionGroupLayout>(dto.Layout, ignoreCase: true, out var l)
            ? l : QuestionGroupLayout.FlatList;
        group.InstructionText = dto.Instruction;
        group.SharedOptionsJson = dto.SharedOptions?.GetRawText();
        group.SharedListTitle = dto.SharedListTitle;
        group.ImagePath = string.IsNullOrWhiteSpace(dto.ImagePath)
            ? null
            : ResolveAssetAbsolutePath(code, dto.ImagePath);
        group.ExampleStem = dto.ExampleStem;
        group.ExampleAnswer = dto.ExampleAnswer;
        group.SummaryTemplate = dto.SummaryTemplate;
    }

    private static void ApplyQuestion(TestQuestion q, ListeningQuestionDto dto)
    {
        if (!Enum.TryParse<QuestionType>(dto.Type, ignoreCase: true, out var qt))
            throw new InvalidOperationException($"Unknown QuestionType '{dto.Type}' in listening question {dto.Order}.");

        q.Type = qt;
        q.Stem = dto.Stem;
        q.OptionsJson = dto.Options.HasValue ? dto.Options.Value.GetRawText() : null;
        q.AnswerKeyJson = dto.AnswerKey.GetRawText();
        q.AcceptableAnswersJson = dto.AcceptableAnswers.HasValue ? dto.AcceptableAnswers.Value.GetRawText() : null;
        q.Points = dto.Points;
        q.WordLimitMax = dto.WordLimitMax;
    }
}
