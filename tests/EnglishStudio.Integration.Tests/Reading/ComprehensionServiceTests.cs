using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EnglishStudio.Integration.Tests.Reading;

public class ComprehensionServiceTests
{
    private const int TextId = 1;

    private static ComprehensionService Make(InMemoryReadingDb db, FakeClaudeCliClient cli, string body = "Some text.") =>
        new(db.Factory, new FakeTextLibraryService(TextId, body), cli, new KeyEchoMessageLocalizer(),
            NullLogger<ComprehensionService>.Instance);

    private static int SeedText(InMemoryReadingDb db)
    {
        using var ctx = db.New();
        var t = new ReadingText { Title = "T", BodyText = "body", CreatedAt = DateTime.UtcNow };
        ctx.ReadingTexts.Add(t);
        ctx.SaveChanges();
        return t.Id;
    }

    private static int SeedMcq(InMemoryReadingDb db, int textId, string[] options, int correct)
    {
        using var ctx = db.New();
        var q = new ComprehensionQuestion
        {
            ReadingTextId = textId,
            Kind = ComprehensionKind.MultipleChoice,
            Prompt = "Q?",
            OptionsJson = JsonSerializer.Serialize(options),
            CorrectOptionIndex = correct,
            OrderIndex = 0
        };
        ctx.ComprehensionQuestions.Add(q);
        ctx.SaveChanges();
        return q.Id;
    }

    [Fact]
    public async Task Mcq_correct_by_index_scores_full()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var qid = SeedMcq(db, textId, ["alpha", "beta", "gamma", "delta"], correct: 2);
        var svc = Make(db, new FakeClaudeCliClient());

        var verdict = await svc.EvaluateAsync(qid, "2");

        Assert.True(verdict.IsCorrect);
        Assert.Equal(100, verdict.Score);
    }

    [Fact]
    public async Task Mcq_correct_by_text_also_accepted()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var qid = SeedMcq(db, textId, ["alpha", "beta", "gamma", "delta"], correct: 2);
        var svc = Make(db, new FakeClaudeCliClient());

        var verdict = await svc.EvaluateAsync(qid, "gamma");

        Assert.True(verdict.IsCorrect);
    }

    [Fact]
    public async Task Mcq_wrong_reports_correct_option()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var qid = SeedMcq(db, textId, ["alpha", "beta", "gamma", "delta"], correct: 2);
        var svc = Make(db, new FakeClaudeCliClient());

        var verdict = await svc.EvaluateAsync(qid, "0");

        Assert.False(verdict.IsCorrect);
        Assert.Equal(0, verdict.Score);
        Assert.Contains("gamma", verdict.FeedbackRu);
    }

    [Fact]
    public async Task GetOrGenerate_returns_cached_even_when_ai_unavailable()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        SeedMcq(db, textId, ["a", "b", "c", "d"], correct: 1);
        var cli = new FakeClaudeCliClient { IsAvailable = false };
        var svc = Make(db, cli);

        var questions = await svc.GetOrGenerateAsync(textId);

        var single = Assert.Single(questions);
        Assert.Equal(ComprehensionKind.MultipleChoice, single.Kind);
        Assert.Equal(4, single.Options.Count);
        Assert.Equal(0, cli.CallCount); // never reached out to Claude
    }

    [Fact]
    public async Task GetOrGenerate_empty_when_no_cache_and_no_ai()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var cli = new FakeClaudeCliClient { IsAvailable = false };
        var svc = Make(db, cli);

        var questions = await svc.GetOrGenerateAsync(textId);

        Assert.Empty(questions);
        Assert.Equal(0, cli.CallCount);
    }

    [Fact]
    public async Task GetOrGenerate_generates_then_caches()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        var cli = new FakeClaudeCliClient
        {
            Responder = _ => """
            [
              {"kind":"mcq","prompt":"What?","options":["a","b","c","d"],"correctIndex":1},
              {"kind":"open","prompt":"Why?","modelAnswer":"because"}
            ]
            """
        };
        var svc = Make(db, cli);

        var first = await svc.GetOrGenerateAsync(textId);
        Assert.Equal(2, first.Count);
        Assert.Equal(ComprehensionKind.MultipleChoice, first[0].Kind);
        Assert.Equal(ComprehensionKind.Open, first[1].Kind);
        Assert.Empty(first[1].Options);
        Assert.Equal(-1, first[1].CorrectOptionIndex);
        Assert.Equal(1, cli.CallCount);

        // Cached on the second call — Claude not invoked again.
        var second = await svc.GetOrGenerateAsync(textId);
        Assert.Equal(2, second.Count);
        Assert.Equal(1, cli.CallCount);

        using var ctx = db.New();
        Assert.Equal(2, ctx.ComprehensionQuestions.Count());
    }

    [Fact]
    public async Task Open_grading_offline_is_unavailable()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        int qid;
        using (var ctx = db.New())
        {
            var q = new ComprehensionQuestion
            {
                ReadingTextId = textId, Kind = ComprehensionKind.Open,
                Prompt = "Why?", CorrectOptionIndex = -1, ModelAnswer = "because", OrderIndex = 0
            };
            ctx.ComprehensionQuestions.Add(q);
            ctx.SaveChanges();
            qid = q.Id;
        }
        var svc = Make(db, new FakeClaudeCliClient { IsAvailable = false });

        var verdict = await svc.EvaluateAsync(qid, "my answer");

        Assert.False(verdict.IsCorrect);
        Assert.Contains("ReadStudy_VerdictOpenOffline", verdict.FeedbackRu);
    }

    [Fact]
    public async Task Open_grading_parses_claude_verdict()
    {
        using var db = new InMemoryReadingDb();
        var textId = SeedText(db);
        int qid;
        using (var ctx = db.New())
        {
            var q = new ComprehensionQuestion
            {
                ReadingTextId = textId, Kind = ComprehensionKind.Open,
                Prompt = "Why?", CorrectOptionIndex = -1, ModelAnswer = "because", OrderIndex = 0
            };
            ctx.ComprehensionQuestions.Add(q);
            ctx.SaveChanges();
            qid = q.Id;
        }
        var cli = new FakeClaudeCliClient
        {
            Responder = _ => """{"isCorrect": true, "score": 85, "feedbackRu": "Хорошо"}"""
        };
        var svc = Make(db, cli);

        var verdict = await svc.EvaluateAsync(qid, "a good answer");

        Assert.True(verdict.IsCorrect);
        Assert.Equal(85, verdict.Score);
        Assert.Equal("Хорошо", verdict.FeedbackRu);
    }
}
