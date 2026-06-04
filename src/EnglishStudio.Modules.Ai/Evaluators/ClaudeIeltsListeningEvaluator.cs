using System.Text;
using System.Text.Json;
using EnglishStudio.Modules.Ai.Reports;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ai.Evaluators;

public sealed class ClaudeIeltsListeningEvaluator : IIeltsListeningEvaluator
{
    private readonly IClaudeCliClient _cli;
    private readonly ILogger<ClaudeIeltsListeningEvaluator> _log;

    public ClaudeIeltsListeningEvaluator(IClaudeCliClient cli, ILogger<ClaudeIeltsListeningEvaluator> log)
    {
        _cli = cli;
        _log = log;
    }

    public async Task<ListeningScoreReport?> EvaluateAsync(
        string testTitle,
        int rawScore,
        int totalQuestions,
        double bandEstimate,
        IReadOnlyList<ListeningPartContext> parts,
        CancellationToken ct = default)
    {
        if (!_cli.IsAvailable) return null;

        var prompt = BuildPrompt(testTitle, rawScore, totalQuestions, bandEstimate, parts);

        var response = await _cli.RunAsync(
            prompt,
            ClaudeOutputFormat.Json,
            timeout: TimeSpan.FromMinutes(4),
            ct: ct);

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            _log.LogWarning("Listening evaluator: empty/error response from Claude CLI.");
            return null;
        }

        return TryParseReport(response.Text);
    }

    private static string BuildPrompt(
        string testTitle,
        int rawScore,
        int totalQuestions,
        double bandEstimate,
        IReadOnlyList<ListeningPartContext> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an experienced IELTS Listening tutor reviewing a completed test attempt.");
        sb.AppendLine("The auto-checker has already determined which answers are correct (matched against");
        sb.AppendLine("the official key). Your job is NOT to re-grade — it is to produce a focused, useful");
        sb.AppendLine("diagnostic so the candidate knows exactly why they missed each question and what to");
        sb.AppendLine("practise next.");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY a single JSON object matching the schema below. No prose, no");
        sb.AppendLine("markdown fence, no comments.");
        sb.AppendLine();
        sb.AppendLine("===== JSON SCHEMA =====");
        sb.AppendLine("""
        {
          "summaryRu": "3-6 предложений на русском: какая часть/тип сильнее, какая слабее, главный actionable шаг",
          "summaryEn": "same summary in English, 3-6 sentences",
          "partInsights": [
            { "partNumber": 1, "partTitle": "...", "commentRu": "1-2 предложения на русском о том, что характерно для ошибок в этой части" }
          ],
          "questionExplanations": [
            {
              "questionNumber": 14,
              "userAnswer": "...",
              "correctAnswer": "...",
              "explanationRu": "Почему правильный ответ именно такой, со ссылкой на конкретную фразу из транскрипта"
            }
          ],
          "tipsRu": ["3-5 практических советов на русском, наиболее приоритетные для исправления слабых мест"]
        }
        """);
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Include `questionExplanations` ONLY for questions the user got wrong. Skip correct ones.");
        sb.AppendLine("2. In each `explanationRu`, quote the relevant phrase from the transcript (in original English) and explain in Russian why it leads to the correct answer.");
        sb.AppendLine("3. In `partInsights`, give one entry per part (4 entries total), even if the user got everything right in a part — say so.");
        sb.AppendLine("4. `tipsRu` must be concrete and actionable (e.g. \"перед прослушиванием подчёркивай ключевые слова в вопросе\", not \"больше слушать\").");
        sb.AppendLine("5. Do NOT comment on the band estimate or invent new scores — the raw score is given for context only.");
        sb.AppendLine();
        sb.AppendLine($"===== TEST META =====");
        sb.AppendLine($"Test: {testTitle}");
        sb.AppendLine($"Raw score: {rawScore} / {totalQuestions}");
        sb.AppendLine($"Estimated band: {bandEstimate:0.0}");
        sb.AppendLine();

        foreach (var part in parts)
        {
            sb.AppendLine($"===== PART {part.PartNumber} — {part.PartTitle} =====");
            sb.AppendLine();
            sb.AppendLine("--- TRANSCRIPT ---");
            sb.AppendLine(string.IsNullOrWhiteSpace(part.Transcript) ? "(transcript not available)" : part.Transcript);
            sb.AppendLine();
            sb.AppendLine("--- QUESTIONS ---");
            foreach (var q in part.Questions)
            {
                var mark = q.IsCorrect ? "✓" : "✗";
                sb.AppendLine($"Q{q.QuestionNumber} [{q.QuestionType}] {mark}");
                sb.AppendLine($"  Stem: {q.Stem}");
                sb.AppendLine($"  User: {(string.IsNullOrWhiteSpace(q.UserAnswer) ? "(empty)" : q.UserAnswer)}");
                sb.AppendLine($"  Key:  {q.CorrectAnswer}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private ListeningScoreReport? TryParseReport(string raw)
    {
        var trimmed = ExtractJsonObject(raw);
        if (trimmed is null) return null;

        try
        {
            return JsonSerializer.Deserialize<ListeningScoreReport>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse ListeningScoreReport JSON: {Raw}", raw[..Math.Min(raw.Length, 400)]);
            return null;
        }
    }

    private static string? ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return (start >= 0 && end > start) ? raw[start..(end + 1)] : null;
    }
}
