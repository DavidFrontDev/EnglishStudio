using System.Collections.Concurrent;
using System.Text.Json;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

public sealed class TestRunner : ITestRunner
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly AnswerCheckerRegistry _checkers;
    private readonly IBandScoreMapper _bandMapper;

    // Serializes upsert operations on the same attempt — the debounced autosave timer and the
    // Finish-time flush can fire concurrently, and without this lock the {FirstOrDefault → Add}
    // pattern can insert two TestAnswer rows for the same (attempt, question) pair.
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _attemptLocks = new();

    public TestRunner(
        IDbContextFactory<IeltsDbContext> dbFactory,
        AnswerCheckerRegistry checkers,
        IBandScoreMapper bandMapper)
    {
        _dbFactory = dbFactory;
        _checkers = checkers;
        _bandMapper = bandMapper;
    }

    public async Task<TestAttempt> StartAsync(int testSetId, bool trainingMode, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var testSet = await db.TestSets.FindAsync(new object[] { testSetId }, ct)
            ?? throw new InvalidOperationException($"TestSet {testSetId} not found.");

        var attempt = new TestAttempt
        {
            TestSetId = testSet.Id,
            StartedAt = DateTime.UtcNow,
            IsTrainingMode = trainingMode
        };
        db.TestAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task SubmitAnswerAsync(int attemptId, int questionId, string userAnswerJson, CancellationToken ct = default)
    {
        var sem = _attemptLocks.GetOrAdd(attemptId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var finishedAt = await db.TestAttempts
                .Where(a => a.Id == attemptId)
                .Select(a => a.FinishedAt)
                .FirstOrDefaultAsync(ct);
            if (finishedAt is not null) return;

            // Include Group so the registry can decide on label-text vs. dropdown routing
            // for Map/DiagramLabeling questions.
            var question = await db.TestQuestions
                .Include(q => q.Group)
                .FirstOrDefaultAsync(q => q.Id == questionId, ct)
                ?? throw new InvalidOperationException($"Question {questionId} not found.");

            var result = _checkers.Check(question, userAnswerJson);

            // Fold any pre-existing duplicates (from legacy data without the dedup lock) into the
            // first row, then delete the rest so the upsert lands on a single canonical row.
            var existing = await db.TestAnswers
                .Where(a => a.TestAttemptId == attemptId && a.TestQuestionId == questionId)
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

            if (existing.Count == 0)
            {
                db.TestAnswers.Add(new TestAnswer
                {
                    TestAttemptId = attemptId,
                    TestQuestionId = questionId,
                    UserAnswerJson = userAnswerJson,
                    IsCorrect = result.IsCorrect,
                    PointsEarned = result.PointsEarned
                });
            }
            else
            {
                existing[0].UserAnswerJson = userAnswerJson;
                existing[0].IsCorrect = result.IsCorrect;
                existing[0].PointsEarned = result.PointsEarned;
                for (var i = 1; i < existing.Count; i++) db.TestAnswers.Remove(existing[i]);
            }
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<TestAttempt> FinishAsync(int attemptId, CancellationToken ct = default)
    {
        var sem = _attemptLocks.GetOrAdd(attemptId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        TestAttempt attempt;
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            attempt = await db.TestAttempts
                .Include(a => a.Answers)
                .Include(a => a.TestSet)
                .FirstOrDefaultAsync(a => a.Id == attemptId, ct)
                ?? throw new InvalidOperationException($"Attempt {attemptId} not found.");

            if (attempt.FinishedAt is null)
            {
                var answeredQuestionIds = attempt.Answers.Select(a => a.TestQuestionId).Distinct().ToList();
                var questions = await db.TestQuestions
                    .AsNoTracking()
                    .Where(q => answeredQuestionIds.Contains(q.Id))
                    .ToListAsync(ct);
                EnforceChooseTwoUniqueness(attempt.Answers, questions);

                attempt.FinishedAt = DateTime.UtcNow;
                attempt.DurationSeconds = (int)(attempt.FinishedAt.Value - attempt.StartedAt).TotalSeconds;
                // Defensive: collapse any legacy duplicate rows so RawScore is not double-counted.
                attempt.RawScore = attempt.Answers
                    .GroupBy(a => a.TestQuestionId)
                    .Sum(g => g.First().PointsEarned);

                if (attempt.TestSet.Section is IeltsSection.Reading or IeltsSection.Listening)
                {
                    attempt.BandEstimate = _bandMapper.RawToBand(
                        attempt.RawScore, attempt.TestSet.Section, attempt.TestSet.Mode);
                }

                await db.SaveChangesAsync(ct);
            }
        }
        finally
        {
            sem.Release();
        }
        _attemptLocks.TryRemove(attemptId, out _);
        return attempt;
    }

    public async Task<TestAttempt?> GetAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TestAttempts
            .Include(a => a.Answers)
            .Include(a => a.TestSet)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);
    }

    public async Task AbandonAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var attempt = await db.TestAttempts.FindAsync(new object[] { attemptId }, ct);
        if (attempt is null || attempt.FinishedAt is not null) return;

        db.TestAttempts.Remove(attempt);
        await db.SaveChangesAsync(ct);
        _attemptLocks.TryRemove(attemptId, out _);
    }

    // IELTS "choose TWO from A–E" is stored as two MultipleChoiceSingle siblings that share the same
    // AcceptableAnswers set. Per-question scoring already credits each pick independently, so a user
    // who selects the same letter twice gets 2/2. Enforce cross-question uniqueness post-finish: within
    // a group of siblings sharing an identical acceptable set, the same letter only counts once.
    private static void EnforceChooseTwoUniqueness(IEnumerable<TestAnswer> answers, IReadOnlyList<TestQuestion> questions)
    {
        var byId = questions.ToDictionary(q => q.Id);

        var groups = answers
            .Select(a => byId.TryGetValue(a.TestQuestionId, out var q) ? (Answer: a, Question: q) : (Answer: a, Question: null!))
            .Where(t => t.Question is not null
                        && t.Question.GroupId is not null
                        && t.Question.Type == QuestionType.MultipleChoiceSingle
                        && !string.IsNullOrWhiteSpace(t.Question.AcceptableAnswersJson))
            .GroupBy(t => t.Question.GroupId!.Value);

        foreach (var grp in groups)
        {
            var rows = grp.ToList();
            if (rows.Count < 2) continue;

            var sets = rows.Select(r => ParseLetterSet(r.Question.AcceptableAnswersJson!)).ToList();
            if (sets[0].Count < 2 || !sets.All(s => s.SetEquals(sets[0]))) continue;

            var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (answer, question) in rows.OrderBy(r => r.Question.OrderInPart).ThenBy(r => r.Question.Id))
            {
                if (!answer.IsCorrect) continue;
                var letter = ParseSingleLetter(answer.UserAnswerJson);
                if (string.IsNullOrEmpty(letter)) continue;
                if (!consumed.Add(letter))
                {
                    answer.IsCorrect = false;
                    answer.PointsEarned = 0;
                }
            }
        }
    }

    private static HashSet<string> ParseLetterSet(string raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return result;
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(raw);
            if (arr is not null)
                foreach (var s in arr)
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s.Trim());
        }
        catch (JsonException) { /* malformed → empty set, group will be skipped */ }
        return result;
    }

    private static string ParseSingleLetter(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('"'))
        {
            try { return (JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty).Trim(); }
            catch (JsonException) { /* fall through */ }
        }
        return trimmed;
    }
}
