using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.IeltsSpeakingAnswerSynth;

public sealed class SpeakingAnswerSynthesizer
{
    private static readonly Regex TopicCodeRegex = new(@"cambridge-(\d+)-test-(\d+)-part-(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IeltsDbContext _db;
    private readonly IClaudeCliClient _cli;
    private readonly ILogger _log;

    public SpeakingAnswerSynthesizer(IeltsDbContext db, IClaudeCliClient cli, ILogger log)
    {
        _db = db;
        _cli = cli;
        _log = log;
    }

    public async Task<AuditResult> AuditAsync(CancellationToken ct)
    {
        var total = await _db.SpeakingQuestions
            .Where(q => q.Bank.TopicCode.StartsWith("cambridge-"))
            .CountAsync(ct);
        var filled = await _db.SpeakingQuestions
            .Where(q => q.Bank.TopicCode.StartsWith("cambridge-") && q.ModelAnswer != null)
            .CountAsync(ct);
        return new AuditResult(total, filled, total - filled);
    }

    public async Task<int> ImportCambridge20Async(
        IReadOnlyList<ParsedAnswer> answers,
        bool dryRun,
        CancellationToken ct)
    {
        var updated = 0;
        var missing = 0;

        foreach (var pa in answers)
        {
            var topicCode = $"cambridge-20-test-{pa.TestNumber}-part-{pa.Part}";
            var question = await _db.SpeakingQuestions
                .Include(q => q.Bank)
                .FirstOrDefaultAsync(q =>
                    q.Bank.TopicCode == topicCode
                    && q.OrderInBank == pa.QuestionIndex,
                    ct);

            if (question is null)
            {
                missing++;
                _log.LogWarning("No DB question for {TopicCode} #{Order}: {Question}",
                    topicCode, pa.QuestionIndex, pa.QuestionText);
                continue;
            }

            if (!string.Equals(question.ModelAnswer, pa.ModelAnswer, StringComparison.Ordinal))
            {
                updated++;
                if (!dryRun)
                {
                    question.ModelAnswer = pa.ModelAnswer;
                }
            }
        }

        if (!dryRun && updated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _log.LogInformation("Cambridge 20 import: parsed {Parsed}, updated {Updated}, missing {Missing}.",
            answers.Count, updated, missing);
        return updated;
    }

    public async Task<int> SynthesizeAsync(SynthesisOptions options, CancellationToken ct)
    {
        var examples = await LoadExamplesAsync(ct);
        if (examples.Count == 0)
        {
            _log.LogError("No Cambridge 20 model answers are present. Run --import-20 first.");
            return 1;
        }

        var targets = await LoadTargetsAsync(options, ct);
        _log.LogInformation("Synthesis targets: {Count}", targets.Count);

        if (options.DryRun)
        {
            foreach (var target in targets.Take(options.Limit ?? targets.Count))
            {
                _log.LogInformation("[dry-run] {TopicCode} #{Order}: {Question}",
                    target.TopicCode, target.QuestionIndex, target.QuestionText);
            }
            return 0;
        }

        var processed = 0;
        var skipped = 0;
        var failed = 0;
        var sw = Stopwatch.StartNew();

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (options.Limit is not null && processed >= options.Limit.Value) break;

            var answer = await GenerateWithRetryAsync(target, examples, options.MaxRetries, ct);
            if (answer is null)
            {
                failed++;
                continue;
            }

            var entity = await _db.SpeakingQuestions.FirstAsync(q => q.Id == target.QuestionId, ct);
            entity.ModelAnswer = answer;
            await _db.SaveChangesAsync(ct);

            processed++;
            _log.LogInformation("[ok] {Done}/{Total} {TopicCode} #{Order} ({Chars} chars)",
                processed, targets.Count, target.TopicCode, target.QuestionIndex, answer.Length);

            if (options.DelayMs > 0)
            {
                await Task.Delay(options.DelayMs, ct);
            }
        }

        _log.LogInformation("Synthesis finished: saved {Saved}, failed {Failed}, skipped {Skipped}, elapsed {Elapsed}.",
            processed, failed, skipped, sw.Elapsed);
        return failed == 0 ? 0 : 2;
    }

    private async Task<string?> GenerateWithRetryAsync(
        SynthesisTarget target,
        IReadOnlyList<ParsedAnswer> allExamples,
        int maxRetries,
        CancellationToken ct)
    {
        var examples = SelectExamples(allExamples, target.Part, target.QuestionId);
        string? lastProblem = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var prompt = BuildPrompt(target, examples, lastProblem);
            var response = await _cli.RunAsync(
                prompt,
                ClaudeOutputFormat.Text,
                timeout: TimeSpan.FromMinutes(3),
                ct: ct);

            if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
            {
                if (IsSessionLimit(response.Text))
                {
                    throw new InvalidOperationException(response.Text.Trim());
                }

                lastProblem = string.IsNullOrWhiteSpace(response.Text)
                    ? "The previous response was empty or an error. Return one answer only."
                    : $"The previous CLI response was an error: {response.Text.Trim()}";
                _log.LogWarning("[retry] {TopicCode} #{Order}: empty/error response.",
                    target.TopicCode, target.QuestionIndex);
                continue;
            }

            var cleaned = CleanAnswer(response.Text);
            var problem = Validate(cleaned, target.Part, examples);
            if (problem is null)
            {
                return cleaned;
            }

            lastProblem = problem;
            _log.LogWarning("[retry] {TopicCode} #{Order}: {Problem}",
                target.TopicCode, target.QuestionIndex, problem);
        }

        _log.LogWarning("[skip] {TopicCode} #{Order}: generation failed validation after retries.",
            target.TopicCode, target.QuestionIndex);
        return null;
    }

    private static string BuildPrompt(
        SynthesisTarget target,
        IReadOnlyList<ParsedAnswer> examples,
        string? retryHint)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert IELTS Speaking trainer.");
        sb.AppendLine("You are given band 7+ candidate answer examples from Cambridge IELTS 20.");
        sb.AppendLine("Match the register: natural spoken English, concise but developed, with light discourse markers like well, actually, I mean, to be honest.");
        sb.AppendLine("Part 1: 1-3 sentences. Part 2: a coherent 60-90 second monologue. Part 3: 2-4 developed sentences with examples.");
        sb.AppendLine("Return only the answer text. Do not include 'A:', markdown, labels, notes, or alternatives.");
        if (!string.IsNullOrWhiteSpace(retryHint))
        {
            sb.AppendLine();
            sb.AppendLine("Fix this issue from the previous attempt:");
            sb.AppendLine(retryHint);
        }

        sb.AppendLine();
        sb.AppendLine("Examples:");
        foreach (var ex in examples)
        {
            sb.AppendLine($"Q: {ex.QuestionText}");
            sb.AppendLine($"A: {ex.ModelAnswer}");
            sb.AppendLine();
        }

        sb.AppendLine("Now generate one band 7+ answer in the same style for this target question:");
        sb.AppendLine($"Part: {target.Part}");
        sb.AppendLine($"Topic: {target.TopicLabel}");
        sb.AppendLine($"Q: {target.PromptQuestionText}");
        return sb.ToString();
    }

    private static string? Validate(string answer, int part, IReadOnlyList<ParsedAnswer> examples)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "Answer is empty.";
        if (LooksMeta(answer)) return "Answer contains meta commentary or a sample-answer prefix.";
        if (NonAsciiRatio(answer) > 0.10) return "Answer should be in English with very little non-ASCII text.";

        var length = answer.Length;
        var (min, max) = part switch
        {
            1 => (50, 450),
            2 => (400, 1800),
            3 => (150, 850),
            _ => (50, 1800)
        };
        if (length < min || length > max)
        {
            return $"Answer length {length} is outside expected range {min}-{max}.";
        }

        var maxSimilarity = examples.Max(e => Similarity(answer, e.ModelAnswer));
        if (maxSimilarity > 0.50)
        {
            return $"Answer is too similar to a calibration example (similarity {maxSimilarity:0.00}).";
        }

        return null;
    }

    private async Task<List<ParsedAnswer>> LoadExamplesAsync(CancellationToken ct)
    {
        var rows = await _db.SpeakingQuestions
            .Include(q => q.Bank)
            .Where(q => q.Bank.TopicCode.StartsWith("cambridge-20-")
                        && q.ModelAnswer != null)
            .OrderBy(q => q.Bank.TopicCode)
            .ThenBy(q => q.OrderInBank)
            .ToListAsync(ct);

        return rows
            .Select(q =>
            {
                var parts = ParseTopicCode(q.Bank.TopicCode);
                return parts is null
                    ? null
                    : new ParsedAnswer(parts.Value.Book, parts.Value.Test, parts.Value.Part,
                        q.OrderInBank, BuildPromptQuestion(q), q.ModelAnswer!);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
    }

    private async Task<List<SynthesisTarget>> LoadTargetsAsync(SynthesisOptions options, CancellationToken ct)
    {
        var rows = await _db.SpeakingQuestions
            .Include(q => q.Bank)
            .Where(q => q.Bank.TopicCode.StartsWith("cambridge-"))
            .OrderBy(q => q.Bank.TopicCode)
            .ThenBy(q => q.OrderInBank)
            .ToListAsync(ct);

        return rows
            .Select(q => (Question: q, Parts: ParseTopicCode(q.Bank.TopicCode)))
            .Where(x => x.Parts is not null)
            .Where(x => x.Parts!.Value.Book >= options.MinBook && x.Parts.Value.Book <= options.MaxBook)
            .Where(x => options.Filters.Count == 0 || options.Filters.Any(f =>
                x.Question.Bank.TopicCode.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            .Where(x => options.Force || string.IsNullOrWhiteSpace(x.Question.ModelAnswer))
            .Select(x => new SynthesisTarget(
                x.Question.Id,
                x.Parts!.Value.Book,
                x.Parts.Value.Test,
                x.Parts.Value.Part,
                x.Question.OrderInBank,
                x.Question.Bank.TopicCode,
                x.Question.Bank.TopicLabel,
                x.Question.Text,
                BuildPromptQuestion(x.Question),
                x.Question.ModelAnswer))
            .ToList();
    }

    private static IReadOnlyList<ParsedAnswer> SelectExamples(
        IReadOnlyList<ParsedAnswer> examples,
        int part,
        int seed)
    {
        var pool = examples.Where(e => e.Part == part).ToList();
        if (pool.Count <= 5) return pool;

        var start = Math.Abs(seed) % pool.Count;
        return Enumerable.Range(0, 5)
            .Select(i => pool[(start + i * 3) % pool.Count])
            .ToList();
    }

    private static string BuildPromptQuestion(SpeakingQuestion q)
    {
        if (q.Bank.Part != SpeakingBankPart.Part2) return q.Text;

        var sb = new StringBuilder();
        sb.AppendLine(q.Bank.CueCardPrompt ?? q.Text);
        var subpoints = ParseSubpoints(q.Bank.CueCardSubpointsJson);
        if (subpoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("You should say:");
            foreach (var subpoint in subpoints)
            {
                sb.AppendLine($"- {subpoint}");
            }
        }
        return sb.ToString().Trim();
    }

    private static List<string> ParseSubpoints(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static (int Book, int Test, int Part)? ParseTopicCode(string topicCode)
    {
        var match = TopicCodeRegex.Match(topicCode);
        if (!match.Success) return null;
        return (
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value));
    }

    private static string CleanAnswer(string raw)
    {
        var text = raw.Trim();
        text = text.Trim('`').Trim();
        if (text.StartsWith("A:", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..].Trim();
        }
        if (text.StartsWith("Answer:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Answer:".Length..].Trim();
        }
        if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 1)
        {
            text = text[1..^1].Trim();
        }
        return text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine).Trim();
    }

    private static bool LooksMeta(string text)
    {
        var lower = text.TrimStart().ToLowerInvariant();
        return lower.StartsWith("here's")
               || lower.StartsWith("here is")
               || lower.StartsWith("sample answer")
               || lower.StartsWith("model answer")
               || lower.StartsWith("sure,");
    }

    private static bool IsSessionLimit(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.Contains("session limit", StringComparison.OrdinalIgnoreCase);

    private static double NonAsciiRatio(string text)
    {
        if (text.Length == 0) return 0;
        return text.Count(c => c > 127) / (double)text.Length;
    }

    private static double Similarity(string left, string right)
    {
        var a = NormalizeForDistance(left);
        var b = NormalizeForDistance(right);
        if (a.Length == 0 && b.Length == 0) return 1;
        var distance = LevenshteinDistance(a, b);
        return 1.0 - distance / (double)Math.Max(a.Length, b.Length);
    }

    private static string NormalizeForDistance(string text) =>
        Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

public sealed record SynthesisOptions(
    int MinBook,
    int MaxBook,
    IReadOnlyList<string> Filters,
    bool Force,
    bool DryRun,
    int? Limit,
    int DelayMs,
    int MaxRetries);

public sealed record AuditResult(int TotalCambridgeQuestions, int FilledModelAnswers, int MissingModelAnswers);
