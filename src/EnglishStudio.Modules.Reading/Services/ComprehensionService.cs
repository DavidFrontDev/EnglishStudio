using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Localization;
using EnglishStudio.Modules.Reading.Data;
using EnglishStudio.Modules.Reading.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Comprehension questions (F2): generates questions for a text via Claude (once, cached in the
/// DB) and grades answers (MCQ locally, Open via Claude). Singleton; uses the
/// <see cref="IDbContextFactory{ReadingDbContext}"/> like the other reading persistence services.
/// </summary>
public sealed class ComprehensionService : IComprehensionService
{
    private const int MaxBodyChars = 8000;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private readonly IDbContextFactory<ReadingDbContext> _factory;
    private readonly ITextLibraryService _library;
    private readonly IClaudeCliClient _cli;
    private readonly IMessageLocalizer _messages;
    private readonly ILogger<ComprehensionService> _log;

    // One gate per text so concurrent opens don't double-generate.
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _gates = new();

    public ComprehensionService(
        IDbContextFactory<ReadingDbContext> factory,
        ITextLibraryService library,
        IClaudeCliClient cli,
        IMessageLocalizer messages,
        ILogger<ComprehensionService> log)
    {
        _factory = factory;
        _library = library;
        _cli = cli;
        _messages = messages;
        _log = log;
    }

    public bool CanUseAi => _cli.IsAvailable;

    public async Task<IReadOnlyList<ComprehensionQuestionDto>> GetOrGenerateAsync(int textId, CancellationToken ct = default)
    {
        var cached = await LoadAsync(textId, ct);
        if (cached.Count > 0) return cached;
        if (!_cli.IsAvailable) return Array.Empty<ComprehensionQuestionDto>();

        var gate = _gates.GetOrAdd(textId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Re-check: another caller may have generated while we waited.
            cached = await LoadAsync(textId, ct);
            if (cached.Count > 0) return cached;

            var detail = await _library.GetAsync(textId, ct);
            if (detail is null || string.IsNullOrWhiteSpace(detail.BodyText))
                return Array.Empty<ComprehensionQuestionDto>();

            var generated = await GenerateAsync(detail.BodyText, ct);
            if (generated.Count == 0) return Array.Empty<ComprehensionQuestionDto>();

            await PersistAsync(textId, generated, ct);
            return await LoadAsync(textId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ComprehensionVerdictDto> EvaluateAsync(int questionId, string userAnswer, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = await db.ComprehensionQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null)
            return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictQuestionNotFound"));

        return q.Kind == ComprehensionKind.MultipleChoice
            ? EvaluateMultipleChoice(q, userAnswer)
            : await EvaluateOpenAsync(q, userAnswer, ct);
    }

    private ComprehensionVerdictDto EvaluateMultipleChoice(ComprehensionQuestion q, string userAnswer)
    {
        var options = DeserializeOptions(q.OptionsJson);
        var answer = (userAnswer ?? string.Empty).Trim();

        // Accept either the option index or the option text.
        var chosen = -1;
        if (int.TryParse(answer, out var idx)) chosen = idx;
        else chosen = options.FindIndex(o => string.Equals(o.Trim(), answer, StringComparison.OrdinalIgnoreCase));

        var correct = chosen >= 0 && chosen == q.CorrectOptionIndex;
        if (correct)
            return new ComprehensionVerdictDto(true, 100, _messages.Format("ReadStudy_VerdictCorrect"));

        var hasKey = q.CorrectOptionIndex >= 0 && q.CorrectOptionIndex < options.Count;
        var feedback = hasKey
            ? _messages.Format("ReadStudy_VerdictWrongWithAnswer", options[q.CorrectOptionIndex])
            : _messages.Format("ReadStudy_VerdictWrong");
        return new ComprehensionVerdictDto(false, 0, feedback);
    }

    private async Task<ComprehensionVerdictDto> EvaluateOpenAsync(ComprehensionQuestion q, string userAnswer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userAnswer))
            return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictEmptyAnswer"));
        if (!_cli.IsAvailable)
            return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictOpenOffline"));

        var prompt = BuildGradePrompt(q.Prompt, q.ModelAnswer, userAnswer);
        try
        {
            var response = await _cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromSeconds(90), ct: ct);
            if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
                return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictEvaluateFailed"));

            var json = ExtractObject(response.Text);
            if (json is null) return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictParseFailed"));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var isCorrect = root.TryGetProperty("isCorrect", out var ic) && ic.ValueKind == JsonValueKind.True;
            var score = root.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var s) ? Math.Clamp(s, 0, 100) : (isCorrect ? 100 : 0);
            var feedback = root.TryGetProperty("feedbackRu", out var fb) ? fb.GetString() ?? "" : "";
            return new ComprehensionVerdictDto(isCorrect, score, string.IsNullOrWhiteSpace(feedback)
                ? (isCorrect ? _messages.Format("ReadStudy_VerdictCorrectShort") : _messages.Format("ReadStudy_VerdictWrong"))
                : feedback);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Open-answer grading failed for question {Id}.", q.Id);
            return new ComprehensionVerdictDto(false, 0, _messages.Format("ReadStudy_VerdictEvaluateFailed"));
        }
    }

    private async Task<IReadOnlyList<ComprehensionQuestionDto>> LoadAsync(int textId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.ComprehensionQuestions
            .Where(q => q.ReadingTextId == textId)
            .OrderBy(q => q.OrderIndex)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private static ComprehensionQuestionDto ToDto(ComprehensionQuestion q) =>
        new(q.Id,
            q.Kind,
            q.Prompt,
            q.Kind == ComprehensionKind.MultipleChoice ? DeserializeOptions(q.OptionsJson) : Array.Empty<string>(),
            q.Kind == ComprehensionKind.MultipleChoice ? q.CorrectOptionIndex : -1);

    private async Task PersistAsync(int textId, IReadOnlyList<ParsedQuestion> questions, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var order = 0;
        foreach (var pq in questions)
        {
            db.ComprehensionQuestions.Add(new ComprehensionQuestion
            {
                ReadingTextId = textId,
                Kind = pq.Kind,
                Prompt = pq.Prompt,
                OptionsJson = pq.Kind == ComprehensionKind.MultipleChoice
                    ? JsonSerializer.Serialize(pq.Options)
                    : null,
                CorrectOptionIndex = pq.Kind == ComprehensionKind.MultipleChoice ? pq.CorrectIndex : -1,
                ModelAnswer = pq.Kind == ComprehensionKind.Open ? pq.ModelAnswer : null,
                OrderIndex = order++
            });
        }
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Generated and cached {Count} comprehension questions for text {TextId}.", questions.Count, textId);
    }

    private async Task<IReadOnlyList<ParsedQuestion>> GenerateAsync(string body, CancellationToken ct)
    {
        var prompt = BuildGeneratePrompt(body);
        ClaudeCliResponse response;
        try
        {
            response = await _cli.RunAsync(prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromSeconds(120), ct: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Comprehension generation call failed.");
            return Array.Empty<ParsedQuestion>();
        }

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
            return Array.Empty<ParsedQuestion>();

        return ParseQuestions(response.Text);
    }

    private IReadOnlyList<ParsedQuestion> ParseQuestions(string raw)
    {
        var json = ExtractArray(raw);
        if (json is null)
        {
            _log.LogWarning("Comprehension JSON array not found in Claude response.");
            return Array.Empty<ParsedQuestion>();
        }

        List<RawQuestion>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<RawQuestion>>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse comprehension questions.");
            return Array.Empty<ParsedQuestion>();
        }

        if (items is null) return Array.Empty<ParsedQuestion>();

        var result = new List<ParsedQuestion>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Prompt)) continue;
            var isMcq = (item.Kind ?? "").Trim().ToLowerInvariant() is "mcq" or "multiplechoice" or "multiple_choice" or "choice";

            if (isMcq)
            {
                var options = item.Options?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList() ?? new List<string>();
                if (options.Count < 2) continue; // not a usable MCQ
                var correct = item.CorrectIndex ?? -1;
                if (correct < 0 || correct >= options.Count) continue; // no usable answer key
                result.Add(new ParsedQuestion(ComprehensionKind.MultipleChoice, item.Prompt!.Trim(), options, correct, null));
            }
            else
            {
                result.Add(new ParsedQuestion(ComprehensionKind.Open, item.Prompt!.Trim(),
                    new List<string>(), -1, string.IsNullOrWhiteSpace(item.ModelAnswer) ? null : item.ModelAnswer!.Trim()));
            }
        }

        return result;
    }

    private static string BuildGeneratePrompt(string body)
    {
        var text = body.Length > MaxBodyChars ? body[..MaxBodyChars] + " […]" : body;
        var sb = new StringBuilder();
        sb.AppendLine("You write reading-comprehension questions for an English learner (IELTS-grade).");
        sb.AppendLine("Read the TEXT and create 3 to 6 questions checking understanding of main ideas and key details.");
        sb.AppendLine("Mix multiple-choice and open questions (at least one of each).");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY a JSON array (no prose, no markdown fence). Element schemas:");
        sb.AppendLine("""  {"kind":"mcq","prompt":"...","options":["A","B","C","D"],"correctIndex":0}""");
        sb.AppendLine("""  {"kind":"open","prompt":"...","modelAnswer":"concise ideal answer"}""");
        sb.AppendLine();
        sb.AppendLine("Rules: exactly 4 options for each mcq; correctIndex is the 0-based correct option.");
        sb.AppendLine("Questions in English, about THIS text only. Keep prompts concise.");
        sb.AppendLine();
        sb.AppendLine("TEXT:");
        sb.AppendLine(text);
        sb.AppendLine();
        sb.AppendLine("Return the JSON array now.");
        return sb.ToString();
    }

    private static string BuildGradePrompt(string question, string? modelAnswer, string userAnswer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You grade a learner's answer to a reading-comprehension question.");
        sb.AppendLine("Compare it to the reference answer: be lenient on wording, strict on meaning.");
        sb.AppendLine("Respond with ONLY a JSON object (no prose, no fence):");
        sb.AppendLine("""  {"isCorrect": true, "score": 0, "feedbackRu": "краткий отзыв по-русски"}""");
        sb.AppendLine("score is 0..100. feedbackRu is one or two short sentences in Russian.");
        sb.AppendLine();
        sb.AppendLine($"Question: {question.Trim()}");
        if (!string.IsNullOrWhiteSpace(modelAnswer))
            sb.AppendLine($"Reference answer: {modelAnswer.Trim()}");
        sb.AppendLine($"Learner's answer: {userAnswer.Trim()}");
        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private static List<string> DeserializeOptions(string? optionsJson)
    {
        if (string.IsNullOrWhiteSpace(optionsJson)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(optionsJson) ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    private static string? ExtractObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }

    private static string? ExtractArray(string raw)
    {
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }

    private sealed record ParsedQuestion(
        ComprehensionKind Kind, string Prompt, List<string> Options, int CorrectIndex, string? ModelAnswer);

    private sealed class RawQuestion
    {
        public string? Kind { get; set; }
        public string? Prompt { get; set; }
        public List<string>? Options { get; set; }
        public int? CorrectIndex { get; set; }
        public string? ModelAnswer { get; set; }
    }
}
