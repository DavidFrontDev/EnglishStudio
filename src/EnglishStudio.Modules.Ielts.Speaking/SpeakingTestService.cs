using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Speaking;

public sealed class SpeakingTestService : ISpeakingTestService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly SpeechMetricsAnalyzer _metrics;
    private readonly ILogger<SpeakingTestService> _log;

    public SpeakingTestService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        SpeechMetricsAnalyzer metrics,
        ILogger<SpeakingTestService> log)
    {
        _dbFactory = dbFactory;
        _metrics = metrics;
        _log = log;
    }

    // ── Banks / topics ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SpeakingTopicSummary>> ListTopicsAsync(SpeakingPart part, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var entityPart = ToEntityPart(part);
        var rows = await db.SpeakingQuestionBanks
            .Where(b => b.Part == entityPart)
            .Select(b => new
            {
                b.Id, b.Part, b.TopicCode, b.TopicLabel, b.CueCardPrompt, b.CueCardSubpointsJson,
                Count = b.Questions.Count
            })
            .OrderBy(b => b.TopicLabel)
            .ToListAsync(ct);

        return rows.Select(r => new SpeakingTopicSummary(
            r.Id, FromEntityPart(r.Part), r.TopicCode, r.TopicLabel, r.CueCardPrompt, r.Count,
            DeserializeSubpoints(r.CueCardSubpointsJson))).ToList();
    }

    public async Task<IReadOnlyList<SpeakingQuestionDetail>> GetQuestionsForBankAsync(int bankId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var qs = await db.SpeakingQuestions
            .Where(q => q.BankId == bankId)
            .OrderBy(q => q.OrderInBank)
            .Select(q => new SpeakingQuestionDetail(q.Id, q.BankId, q.OrderInBank, q.Text, q.FollowUpToQuestionId))
            .ToListAsync(ct);
        return qs;
    }

    public async Task<SpeakingTopicSummary?> PickRandomTopicAsync(SpeakingPart part, CancellationToken ct = default)
    {
        var all = await ListTopicsAsync(part, ct);
        if (all.Count == 0) return null;
        return all[Random.Shared.Next(all.Count)];
    }

    public async Task<FullMockBundle> StartFullMockAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Part 2 cue card: random bank that has at least one question.
        var p2Bank = await PickRandomBankWithQuestionsAsync(db, SpeakingBankPart.Part2, ct)
            ?? throw new InvalidOperationException("Speaking seed is empty — no Part 2 banks available.");

        return await BuildFullMockAsync(db, p2Bank, bookAligned: false, ct);
    }

    public async Task<FullMockBundle> StartFullMockAsync(int part2BankId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var p2Bank = await db.SpeakingQuestionBanks
            .Include(b => b.Questions)
            .FirstOrDefaultAsync(b => b.Id == part2BankId && b.Part == SpeakingBankPart.Part2, ct);

        // Указанный банк недоступен — мягко деградируем к случайному набору (mock остаётся валидным).
        if (p2Bank is null || p2Bank.Questions.Count == 0)
        {
            _log.LogWarning("Speaking full-mock: Part 2 bank {Id} missing/empty — falling back to random.", part2BankId);
            p2Bank = await PickRandomBankWithQuestionsAsync(db, SpeakingBankPart.Part2, ct)
                ?? throw new InvalidOperationException("Speaking seed is empty — no Part 2 banks available.");
            return await BuildFullMockAsync(db, p2Bank, bookAligned: false, ct);
        }

        return await BuildFullMockAsync(db, p2Bank, bookAligned: true, ct);
    }

    /// <summary>
    /// Собирает full-mock-набор вокруг уже выбранного Part 2 банка. При <paramref name="bookAligned"/>
    /// Part 1/Part 3 берутся из того же Cambridge-теста (по коду <c>cambridge-{book}-test-{n}-part-N</c>);
    /// иначе Part 1 — микс из 5 вопросов разных банков, Part 3 — только по явной связи или случайный.
    /// </summary>
    private async Task<FullMockBundle> BuildFullMockAsync(
        IeltsDbContext db, SpeakingQuestionBank p2Bank, bool bookAligned, CancellationToken ct)
    {
        var p2Question = p2Bank.Questions.OrderBy(q => q.OrderInBank).First();

        // Part 3 follow-ups: bank explicitly linked to this Part 2 by id. Book-aligned пробует также
        // совпадение по коду перед общим случайным фолбэком.
        var p3Bank = await db.SpeakingQuestionBanks
            .Include(b => b.Questions)
            .Where(b => b.Part == SpeakingBankPart.Part3 && b.LinkedPart2BankId == p2Bank.Id)
            .FirstOrDefaultAsync(ct);

        if (p3Bank is null && bookAligned && ReplacePartSuffix(p2Bank.TopicCode, 3) is string p3Code)
        {
            p3Bank = await db.SpeakingQuestionBanks
                .Include(b => b.Questions)
                .FirstOrDefaultAsync(b => b.Part == SpeakingBankPart.Part3 && b.TopicCode == p3Code, ct);
        }

        p3Bank ??= await PickRandomBankWithQuestionsAsync(db, SpeakingBankPart.Part3, ct);
        var p3Questions = p3Bank?.Questions.OrderBy(q => q.OrderInBank).ToList() ?? new();

        // Part 1: book-aligned → весь банк того же теста (аутентичный набор вопросов); иначе/фолбэк →
        // 5 вопросов, выбранных из 2-3 разных банков.
        List<SpeakingQuestion> p1Picks = new();
        if (bookAligned && ReplacePartSuffix(p2Bank.TopicCode, 1) is string p1Code)
        {
            var p1Bank = await db.SpeakingQuestionBanks
                .Include(b => b.Questions)
                .FirstOrDefaultAsync(b => b.Part == SpeakingBankPart.Part1 && b.TopicCode == p1Code, ct);
            if (p1Bank is { Questions.Count: > 0 })
                p1Picks = p1Bank.Questions.OrderBy(q => q.OrderInBank).ToList();
        }

        if (p1Picks.Count == 0)
        {
            var p1Pool = await db.SpeakingQuestions
                .Include(q => q.Bank)
                .Where(q => q.Bank.Part == SpeakingBankPart.Part1)
                .ToListAsync(ct);
            p1Picks = PickPart1Mix(p1Pool, 5);
        }

        return new FullMockBundle(
            Part1Questions: p1Picks.Select(MapQuestion).ToList(),
            Part2Topic: MapTopic(p2Bank),
            Part2Question: MapQuestion(p2Question),
            Part3FollowUps: p3Questions.Select(MapQuestion).ToList());
    }

    /// <summary>"cambridge-15-test-2-part-2" → "cambridge-15-test-2-part-{n}"; null если суффикс не найден.</summary>
    private static string? ReplacePartSuffix(string topicCode, int part)
    {
        const string marker = "-part-";
        var idx = topicCode.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? null : string.Concat(topicCode.AsSpan(0, idx), marker, part.ToString());
    }

    // ── Attempt lifecycle ─────────────────────────────────────────────────────

    public async Task<int> StartAttemptAsync(SpeakingMode mode, int? topicBankId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = new SpeakingAttempt
        {
            Mode = ToEntityMode(mode),
            TopicBankId = topicBankId,
            StartedAt = DateTime.UtcNow
        };
        db.SpeakingAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt.Id;
    }

    public async Task SaveResponseAsync(
        int attemptId, int questionId, string audioPath, string? transcript,
        int durationSeconds, IReadOnlyList<SpokenWord>? words = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(audioPath))
            throw new ArgumentException("audioPath is required.", nameof(audioPath));

        // Metrics computed off the persistence boundary so the DbContext isn't held while we
        // touch the WAV file. When the caller passes Whisper word timestamps we get precise
        // WPM (active span) and pause ratio (inter-word gaps); otherwise the analyzer falls
        // back to NAudio RMS-based silence detection on the WAV.
        var metrics = await _metrics.ComputeAsync(
            audioPath,
            transcript ?? string.Empty,
            words ?? Array.Empty<SpokenWord>(),
            durationSeconds,
            ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existingOrder = await db.SpeakingResponses
            .Where(r => r.SpeakingAttemptId == attemptId)
            .Select(r => (int?)r.OrderInAttempt)
            .MaxAsync(ct) ?? 0;

        var response = new SpeakingResponse
        {
            SpeakingAttemptId = attemptId,
            SpeakingQuestionId = questionId,
            OrderInAttempt = existingOrder + 1,
            AudioPath = audioPath,
            Transcript = transcript,
            DurationSeconds = durationSeconds,
            WpmRate = metrics.WordsPerMinute,
            PauseRatio = metrics.PauseRatio,
            FillerCount = metrics.FillerCount,
            TypeTokenRatio = metrics.TypeTokenRatio
        };

        db.SpeakingResponses.Add(response);
        await db.SaveChangesAsync(ct);
    }

    public async Task FinishAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.SpeakingAttempts.FindAsync(new object?[] { attemptId }, ct);
        if (attempt is null) return;
        attempt.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ── History ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SpeakingAttemptSummary>> ListAttemptsAsync(int limit = 50, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.SpeakingAttempts
            .OrderByDescending(a => a.StartedAt)
            .Take(limit)
            .Select(a => new SpeakingAttemptSummary(
                a.Id, FromEntityMode(a.Mode), a.StartedAt, a.FinishedAt, a.BandOverall))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<SpeakingAttemptDetail?> GetAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.SpeakingAttempts
            .Include(a => a.Responses.OrderBy(r => r.OrderInAttempt))
                .ThenInclude(r => r.Question)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return null;

        var summary = new SpeakingAttemptSummary(
            attempt.Id, FromEntityMode(attempt.Mode), attempt.StartedAt, attempt.FinishedAt, attempt.BandOverall);

        var responses = attempt.Responses
            .Select(r => new SpeakingResponseDetail(
                r.Id, r.SpeakingQuestionId, r.Question.Text, r.AudioPath, r.Transcript, r.DurationSeconds,
                r.WpmRate, r.PauseRatio, r.FillerCount, r.TypeTokenRatio))
            .ToList();

        return new SpeakingAttemptDetail(
            summary, responses,
            attempt.BandFluencyCoherence, attempt.BandLexicalResource,
            attempt.BandGrammar, attempt.BandPronunciation, attempt.FeedbackJson);
    }

    public async Task DeleteAttemptAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.SpeakingAttempts
            .Include(a => a.Responses)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
        if (attempt is null) return;

        TryDeleteAudioFiles(attempt);
        db.SpeakingAttempts.Remove(attempt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ClearHistoryAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var all = await db.SpeakingAttempts.Include(a => a.Responses).ToListAsync(ct);
        foreach (var a in all) TryDeleteAudioFiles(a);

        var n = all.Count;
        db.SpeakingAttempts.RemoveRange(all);
        await db.SaveChangesAsync(ct);
        return n;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TryDeleteAudioFiles(SpeakingAttempt attempt)
    {
        // Wipe per-attempt audio folder if everything came from the standard location.
        var dirs = attempt.Responses
            .Select(r => Path.GetDirectoryName(r.AudioPath))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var d in dirs)
        {
            try
            {
                if (Directory.Exists(d)) Directory.Delete(d, recursive: true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to delete speaking audio dir {Dir}", d);
            }
        }
    }

    private static async Task<SpeakingQuestionBank?> PickRandomBankWithQuestionsAsync(
        IeltsDbContext db, SpeakingBankPart part, CancellationToken ct)
    {
        var ids = await db.SpeakingQuestionBanks
            .Where(b => b.Part == part && b.Questions.Any())
            .Select(b => b.Id)
            .ToListAsync(ct);
        if (ids.Count == 0) return null;
        var pick = ids[Random.Shared.Next(ids.Count)];
        return await db.SpeakingQuestionBanks
            .Include(b => b.Questions)
            .FirstAsync(b => b.Id == pick, ct);
    }

    private static List<SpeakingQuestion> PickPart1Mix(List<SpeakingQuestion> pool, int count)
    {
        // Try to spread across 2-3 distinct banks. Group, shuffle banks, then take up to 2 q
        // per bank until we have `count`, falling back to any remaining if the pool is small.
        var byBank = pool.GroupBy(q => q.BankId).ToList();
        var bankOrder = byBank.OrderBy(_ => Random.Shared.Next()).ToList();

        var picks = new List<SpeakingQuestion>();
        foreach (var bank in bankOrder)
        {
            var taken = bank.OrderBy(_ => Random.Shared.Next()).Take(2);
            foreach (var q in taken)
            {
                if (picks.Count >= count) break;
                picks.Add(q);
            }
            if (picks.Count >= count) break;
        }

        // Fill remaining from the raw pool if not enough.
        foreach (var q in pool.OrderBy(_ => Random.Shared.Next()))
        {
            if (picks.Count >= count) break;
            if (picks.Contains(q)) continue;
            picks.Add(q);
        }
        return picks;
    }

    private static SpeakingTopicSummary MapTopic(SpeakingQuestionBank b) =>
        new(b.Id, FromEntityPart(b.Part), b.TopicCode, b.TopicLabel, b.CueCardPrompt, b.Questions.Count,
            DeserializeSubpoints(b.CueCardSubpointsJson));

    private static IReadOnlyList<string>? DeserializeSubpoints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return list is { Count: > 0 } ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static SpeakingQuestionDetail MapQuestion(SpeakingQuestion q) =>
        new(q.Id, q.BankId, q.OrderInBank, q.Text, q.FollowUpToQuestionId);

    private static SpeakingBankPart ToEntityPart(SpeakingPart p) => p switch
    {
        SpeakingPart.Part1 => SpeakingBankPart.Part1,
        SpeakingPart.Part2 => SpeakingBankPart.Part2,
        SpeakingPart.Part3 => SpeakingBankPart.Part3,
        _ => SpeakingBankPart.Part1
    };

    private static SpeakingPart FromEntityPart(SpeakingBankPart p) => p switch
    {
        SpeakingBankPart.Part1 => SpeakingPart.Part1,
        SpeakingBankPart.Part2 => SpeakingPart.Part2,
        SpeakingBankPart.Part3 => SpeakingPart.Part3,
        _ => SpeakingPart.Part1
    };

    private static SpeakingAttemptMode ToEntityMode(SpeakingMode m) => m switch
    {
        SpeakingMode.FullMock => SpeakingAttemptMode.FullMock,
        SpeakingMode.Part1Only => SpeakingAttemptMode.Part1Only,
        SpeakingMode.Part2Only => SpeakingAttemptMode.Part2Only,
        SpeakingMode.Part3Only => SpeakingAttemptMode.Part3Only,
        _ => SpeakingAttemptMode.FullMock
    };

    private static SpeakingMode FromEntityMode(SpeakingAttemptMode m) => m switch
    {
        SpeakingAttemptMode.FullMock => SpeakingMode.FullMock,
        SpeakingAttemptMode.Part1Only => SpeakingMode.Part1Only,
        SpeakingAttemptMode.Part2Only => SpeakingMode.Part2Only,
        SpeakingAttemptMode.Part3Only => SpeakingMode.Part3Only,
        _ => SpeakingMode.FullMock
    };
}
