using System.IO;
using System.Text;
using System.Text.Json;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ai.Rubrics;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ai.Evaluators;

public sealed class ClaudeIeltsEssayEvaluator : IIeltsEssayEvaluator
{
    private readonly IClaudeCliClient _cli;
    private readonly ILogger<ClaudeIeltsEssayEvaluator> _log;

    public ClaudeIeltsEssayEvaluator(IClaudeCliClient cli, ILogger<ClaudeIeltsEssayEvaluator> log)
    {
        _cli = cli;
        _log = log;
    }

    public async Task<EssayScoreReport?> EvaluateAsync(
        WritingTaskType taskType,
        string prompt,
        string userEssay,
        IReadOnlyList<EssayReferenceExample>? referenceExamples = null,
        string? taskImagePath = null,
        CancellationToken ct = default)
    {
        if (!_cli.IsAvailable) return null;

        var hasImage = !string.IsNullOrWhiteSpace(taskImagePath) && File.Exists(taskImagePath);
        if (!string.IsNullOrWhiteSpace(taskImagePath) && !hasImage)
        {
            _log.LogWarning("Essay evaluator: image path provided but file missing — falling back to text-only: {Path}", taskImagePath);
        }

        var fullPrompt = BuildPrompt(taskType, prompt, userEssay, referenceExamples, hasImage);
        var images = hasImage ? new[] { taskImagePath! } : null;

        var response = await _cli.RunAsync(
            fullPrompt,
            ClaudeOutputFormat.Json,
            timeout: TimeSpan.FromMinutes(3),
            imagePaths: images,
            ct: ct);

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            _log.LogWarning("Essay evaluator got empty/error response from Claude CLI.");
            return null;
        }

        return TryParseReport(response.Text);
    }

    private static string BuildPrompt(
        WritingTaskType taskType,
        string prompt,
        string userEssay,
        IReadOnlyList<EssayReferenceExample>? referenceExamples,
        bool hasImage)
    {
        var taskLabel = taskType switch
        {
            WritingTaskType.Task1Academic => "Academic Writing Task 1 (150 words, 20 minutes)",
            WritingTaskType.Task1GeneralTraining => "General Training Writing Task 1 — letter (150 words, 20 minutes)",
            WritingTaskType.Task2 => "Writing Task 2 — essay (250 words, 40 minutes)",
            _ => "IELTS Writing task"
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are a certified IELTS Writing examiner. Score the user's essay strictly");
        sb.AppendLine("by the public band descriptors below. Respond with ONLY a single JSON object");
        sb.AppendLine("matching the schema in the rubric. No prose, no markdown fence, no comments.");
        sb.AppendLine();
        sb.AppendLine("===== RUBRIC =====");
        sb.AppendLine(RubricLoader.Writing);
        sb.AppendLine();
        sb.AppendLine($"===== TASK TYPE: {taskLabel} =====");
        sb.AppendLine();
        sb.AppendLine("===== TASK PROMPT =====");
        sb.AppendLine(prompt);
        sb.AppendLine();

        if (referenceExamples is { Count: > 0 })
        {
            sb.AppendLine("===== SCORED REFERENCE ESSAYS FOR THIS EXACT PROMPT =====");
            sb.AppendLine("Below are real candidate responses to the SAME prompt, each pre-scored by a");
            sb.AppendLine("certified examiner with the official band score and an explanatory comment.");
            sb.AppendLine("Use them as calibration anchors: the user's essay below must be scored on the");
            sb.AppendLine("SAME scale. If the user's work is comparable in quality to a Band X reference,");
            sb.AppendLine("award close to Band X. Do not be generous beyond what these anchors justify.");
            sb.AppendLine();
            foreach (var ex in referenceExamples)
            {
                sb.AppendLine($"--- Reference: Band {ex.BandLevel}.0 candidate ---");
                sb.AppendLine(ex.AnswerText);
                if (!string.IsNullOrWhiteSpace(ex.ExaminerComment))
                {
                    sb.AppendLine();
                    sb.AppendLine("Examiner's comment on this Band " + ex.BandLevel + " response:");
                    sb.AppendLine(ex.ExaminerComment);
                }
                sb.AppendLine();
            }
        }

        if (hasImage)
        {
            sb.AppendLine("===== TASK 1 CHART/DIAGRAM =====");
            sb.AppendLine("The visual prompt for this task is attached as an image (referenced at the");
            sb.AppendLine("top of this message via @<path>). Use the data points, labels, trends and");
            sb.AppendLine("structural elements visible in the image to verify whether the user's essay");
            sb.AppendLine("accurately describes them. Penalise inaccurate numbers, omitted key features,");
            sb.AppendLine("and misidentified trends under Task Achievement. Do not let factual errors");
            sb.AppendLine("about the visual slide on the grounds that the writing is otherwise fluent.");
            sb.AppendLine();
        }

        sb.AppendLine("===== USER ESSAY =====");
        sb.AppendLine(userEssay);
        sb.AppendLine();
        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private EssayScoreReport? TryParseReport(string raw)
    {
        var trimmed = ExtractJsonObject(raw);
        if (trimmed is null) return null;

        try
        {
            return JsonSerializer.Deserialize<EssayScoreReport>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse EssayScoreReport JSON: {Raw}", raw[..Math.Min(raw.Length, 300)]);
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
