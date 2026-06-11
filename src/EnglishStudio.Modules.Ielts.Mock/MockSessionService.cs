using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using EnglishStudio.Modules.Ielts.Speaking;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Mock;

/// <summary>
/// Оркестратор mock-экзамена. Управляет состоянием 4 секций (Listening→Reading→Writing→Speaking),
/// но НЕ создаёт дочерние attempt'ы — секционные VM стартуют их сами; оркестратор линкует id и band
/// через <see cref="CompleteSectionAsync"/>. Overall считается через <see cref="OverallBandCalculator"/>.
/// </summary>
public sealed class MockSessionService : IMockSessionService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly MockBundlePicker _bundlePicker;
    private readonly ITestRunner _testRunner;          // Reading/Listening attempts (общий TestAttempt)
    private readonly IWritingTaskService _writing;
    private readonly ISpeakingTestService _speaking;

    private static readonly MockSection[] AllSections =
        { MockSection.Listening, MockSection.Reading, MockSection.Writing, MockSection.Speaking };

    public MockSessionService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        MockBundlePicker bundlePicker,
        ITestRunner testRunner,
        IWritingTaskService writing,
        ISpeakingTestService speaking)
    {
        _dbFactory = dbFactory;
        _bundlePicker = bundlePicker;
        _testRunner = testRunner;
        _writing = writing;
        _speaking = speaking;
    }

    /// <summary>Состояние секции в SectionsJson. SourceId — testSetId/bankId для запуска секции.</summary>
    private sealed record SectionStateDto(
        int Section,
        int Status,
        int? SourceId,
        int? ChildAttemptId,
        int? SecondaryChildAttemptId,   // Writing Task2 attempt; null для остальных секций
        DateTime? StartedAt,
        DateTime? FinishedAt,
        double? Band);

    public Task<IReadOnlyList<MockBundleSummary>> ListAvailableBundlesAsync(CancellationToken ct = default)
        => _bundlePicker.ListAsync(ct);

    public Task<MockBundleSummary?> PickRandomBundleAsync(CancellationToken ct = default)
        => _bundlePicker.PickRandomAsync(ct);

    public async Task<int> StartAttemptAsync(MockMode mode, MockBundleSummary? bundle, CancellationToken ct = default)
    {
        if (bundle is null)
            throw new ArgumentNullException(nameof(bundle), "v1 requires a bundle (Custom mode is not implemented).");

        var sections = AllSections.Select(s =>
        {
            int? sourceId = SourceIdFor(bundle, s);
            // Секция без source-id (нет контента в бандле) сразу помечается Skipped.
            var status = sourceId.HasValue ? MockSectionStatus.Pending : MockSectionStatus.Skipped;
            return new SectionStateDto((int)s, (int)status, sourceId, null, null, null, null, null);
        }).ToList();

        var attempt = new MockAttempt
        {
            ModeCode = mode.ToString(),
            Book = bundle.Book,
            TestNumber = bundle.TestNumber,
            StartedAt = DateTime.UtcNow,
            CurrentSection = (int?)FirstPending(sections),
            SectionsJson = JsonSerializer.Serialize(sections),
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.MockAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }

    public async Task<int> BeginSectionAsync(int mockAttemptId, MockSection section, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await LoadAsync(db, mockAttemptId, ct);

        var sections = Deserialize(attempt.SectionsJson);
        var idx = sections.FindIndex(s => s.Section == (int)section);
        if (idx < 0) throw new InvalidOperationException($"Section {section} is missing in mock {mockAttemptId}.");

        var state = sections[idx];
        if (state.SourceId is null)
            throw new InvalidOperationException($"Section {section} is unavailable in this bundle.");

        // Идемпотентно: повторный вход в InProgress-секцию не сбрасывает StartedAt.
        if (state.Status != (int)MockSectionStatus.InProgress)
        {
            sections[idx] = state with
            {
                Status = (int)MockSectionStatus.InProgress,
                StartedAt = state.StartedAt ?? DateTime.UtcNow,
            };
            attempt.SectionsJson = JsonSerializer.Serialize(sections);
        }

        attempt.CurrentSection = (int)section;
        await db.SaveChangesAsync(ct);
        return state.SourceId.Value;
    }

    public async Task CompleteSectionAsync(int mockAttemptId, MockSection section, int childAttemptId, double? band, int? secondaryChildAttemptId = null, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await LoadAsync(db, mockAttemptId, ct);

        var sections = Deserialize(attempt.SectionsJson);
        var idx = sections.FindIndex(s => s.Section == (int)section);
        if (idx < 0) throw new InvalidOperationException($"Section {section} is missing in mock {mockAttemptId}.");

        sections[idx] = sections[idx] with
        {
            Status = (int)MockSectionStatus.Completed,
            ChildAttemptId = childAttemptId,
            SecondaryChildAttemptId = secondaryChildAttemptId,
            FinishedAt = DateTime.UtcNow,
            Band = band,
        };
        attempt.SectionsJson = JsonSerializer.Serialize(sections);

        LinkChildAttempt(attempt, section, childAttemptId);
        SetSectionBand(attempt, section, band);
        attempt.CurrentSection = (int?)FirstPending(sections);

        await db.SaveChangesAsync(ct);
    }

    public async Task SkipSectionAsync(int mockAttemptId, MockSection section, string reason, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await LoadAsync(db, mockAttemptId, ct);

        var sections = Deserialize(attempt.SectionsJson);
        var idx = sections.FindIndex(s => s.Section == (int)section);
        if (idx < 0) throw new InvalidOperationException($"Section {section} is missing in mock {mockAttemptId}.");

        // reason пока не персистится (зарезервировано под будущий audit-лог).
        sections[idx] = sections[idx] with
        {
            Status = (int)MockSectionStatus.Skipped,
            FinishedAt = DateTime.UtcNow,
        };
        attempt.SectionsJson = JsonSerializer.Serialize(sections);
        attempt.CurrentSection = (int?)FirstPending(sections);

        await db.SaveChangesAsync(ct);
    }

    public async Task<double> FinaliseAsync(int mockAttemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await LoadAsync(db, mockAttemptId, ct);

        // Подтягиваем band'ы из дочерних attempt'ов, если кэш пуст (AI-оценка Writing/Speaking
        // могла завершиться уже после CompleteSectionAsync).
        attempt.ListeningBand ??= await PullTestBandAsync(attempt.ListeningAttemptId, ct);
        attempt.ReadingBand ??= await PullTestBandAsync(attempt.ReadingAttemptId, ct);
        attempt.WritingBand ??= await PullWritingBandAsync(attempt, ct);
        attempt.SpeakingBand ??= await PullSpeakingBandAsync(attempt.SpeakingAttemptId, ct);

        var bands = new List<double>(4);
        if (attempt.ListeningBand is double l) bands.Add(l);
        if (attempt.ReadingBand is double r) bands.Add(r);
        if (attempt.WritingBand is double w) bands.Add(w);
        if (attempt.SpeakingBand is double s) bands.Add(s);

        double? overall = bands.Count switch
        {
            4 => OverallBandCalculator.Calculate(
                    attempt.ListeningBand!.Value, attempt.ReadingBand!.Value,
                    attempt.WritingBand!.Value, attempt.SpeakingBand!.Value),
            >= 1 => OverallBandCalculator.RoundToOfficialBand(bands.Average()),
            _ => null,
        };

        attempt.OverallBand = overall;
        attempt.IsPartial = bands.Count < 4;
        attempt.FinishedAt = DateTime.UtcNow;
        attempt.CurrentSection = null;

        await db.SaveChangesAsync(ct);
        return overall ?? 0.0;
    }

    public async Task<MockAttemptDetail?> GetAsync(int mockAttemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var a = await db.MockAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == mockAttemptId, ct);
        if (a is null) return null;

        var sections = Deserialize(a.SectionsJson)
            .Select(d => new MockSectionState(
                (MockSection)d.Section, (MockSectionStatus)d.Status,
                d.StartedAt, d.FinishedAt, d.ChildAttemptId, d.SecondaryChildAttemptId, d.Band))
            .ToList();

        return new MockAttemptDetail(
            ToSummary(a), a.ListeningAttemptId, a.ReadingAttemptId,
            a.WritingAttemptId, a.SpeakingAttemptId, sections);
    }

    public async Task<MockAttemptSummary?> FindResumableAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var a = await db.MockAttempts.AsNoTracking()
            .Where(x => x.FinishedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);
        return a is null ? null : ToSummary(a);
    }

    public async Task<IReadOnlyList<MockAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var list = await db.MockAttempts.AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Take(limit)
            .ToListAsync(ct);
        return list.Select(ToSummary).ToList();
    }

    public async Task DeleteAsync(int mockAttemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var a = await db.MockAttempts.FindAsync([mockAttemptId], ct);
        if (a is null) return;
        db.MockAttempts.Remove(a);
        await db.SaveChangesAsync(ct);   // дочерние attempt'ы — principal-сторона, не трогаются
    }

    public async Task<int> ClearHistoryAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.MockAttempts.ExecuteDeleteAsync(ct);
    }

    // ---- helpers ----

    private static async Task<MockAttempt> LoadAsync(IeltsDbContext db, int id, CancellationToken ct)
        => await db.MockAttempts.FindAsync([id], ct)
           ?? throw new InvalidOperationException($"MockAttempt {id} not found.");

    private static int? SourceIdFor(MockBundleSummary b, MockSection s) => s switch
    {
        MockSection.Listening => b.ListeningTestId,
        MockSection.Reading => b.ReadingTestSetId,
        MockSection.Writing => b.WritingTestSetId,
        MockSection.Speaking => b.SpeakingPart2BankId,
        _ => null,
    };

    private static void LinkChildAttempt(MockAttempt a, MockSection s, int childId)
    {
        switch (s)
        {
            case MockSection.Listening: a.ListeningAttemptId = childId; break;
            case MockSection.Reading: a.ReadingAttemptId = childId; break;
            case MockSection.Writing: a.WritingAttemptId = childId; break;
            case MockSection.Speaking: a.SpeakingAttemptId = childId; break;
        }
    }

    private static void SetSectionBand(MockAttempt a, MockSection s, double? band)
    {
        switch (s)
        {
            case MockSection.Listening: a.ListeningBand = band; break;
            case MockSection.Reading: a.ReadingBand = band; break;
            case MockSection.Writing: a.WritingBand = band; break;
            case MockSection.Speaking: a.SpeakingBand = band; break;
        }
    }

    private static List<SectionStateDto> Deserialize(string json)
        => JsonSerializer.Deserialize<List<SectionStateDto>>(json) ?? [];

    /// <summary>Первая ещё не пройденная (Pending) секция в порядке L→R→W→S, либо null.</summary>
    private static MockSection? FirstPending(List<SectionStateDto> s)
    {
        var first = s.FirstOrDefault(x => x.Status == (int)MockSectionStatus.Pending);
        return first is null ? null : (MockSection)first.Section;
    }

    private static MockAttemptSummary ToSummary(MockAttempt a) => new(
        AttemptId: a.Id,
        Mode: Enum.TryParse<MockMode>(a.ModeCode, out var m) ? m : MockMode.CambridgeBundle,
        Book: a.Book,
        TestNumber: a.TestNumber,
        StartedAt: a.StartedAt,
        FinishedAt: a.FinishedAt,
        CurrentSection: a.CurrentSection is int cs ? (MockSection)cs : null,
        OverallBand: a.OverallBand,
        ListeningBand: a.ListeningBand,
        ReadingBand: a.ReadingBand,
        WritingBand: a.WritingBand,
        SpeakingBand: a.SpeakingBand,
        IsPartial: a.IsPartial);

    private async Task<double?> PullTestBandAsync(int? attemptId, CancellationToken ct)
    {
        if (attemptId is not int id) return null;
        var ta = await _testRunner.GetAsync(id, ct);
        return ta is { IsCompleted: true } ? ta.BandEstimate : null;
    }

    private async Task<double?> PullWritingBandAsync(MockAttempt attempt, CancellationToken ct)
    {
        if (attempt.WritingAttemptId is not int id) return null;
        var t1 = (await _writing.GetAttemptAsync(id, ct))?.BandOverall;

        var secondaryId = Deserialize(attempt.SectionsJson)
            .FirstOrDefault(s => s.Section == (int)MockSection.Writing)?.SecondaryChildAttemptId;
        if (secondaryId is not int sid) return t1;

        var t2 = (await _writing.GetAttemptAsync(sid, ct))?.BandOverall;
        if (t1 is not double b1 || t2 is not double b2) return null;

        // Task1·1/3 + Task2·2/3, округление к ближайшей половине — как WeightedWritingBand в UI.
        var raw = (b1 + 2 * b2) / 3.0;
        return Math.Round(raw * 2) / 2;
    }

    private async Task<double?> PullSpeakingBandAsync(int? attemptId, CancellationToken ct)
    {
        if (attemptId is not int id) return null;
        var sa = await _speaking.GetAttemptAsync(id, ct);
        return sa?.Summary.BandOverall;
    }
}
