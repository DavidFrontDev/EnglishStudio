using System.Text;
using System.Text.Json;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ai.Rubrics;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ai.Evaluators;

public sealed class ClaudeIeltsSpeakingEvaluator : IIeltsSpeakingEvaluator
{
    private readonly IClaudeCliClient _cli;
    private readonly ILogger<ClaudeIeltsSpeakingEvaluator> _log;

    public ClaudeIeltsSpeakingEvaluator(IClaudeCliClient cli, ILogger<ClaudeIeltsSpeakingEvaluator> log)
    {
        _cli = cli;
        _log = log;
    }

    public async Task<SpeakingScoreReport?> EvaluateAsync(
        SpeakingPartType partType,
        string? topic,
        IReadOnlyList<SpeakingTurn> turns,
        SpeakingMetrics metrics,
        CancellationToken ct = default)
    {
        if (!_cli.IsAvailable) return null;
        if (turns.Count == 0) return null;

        var prompt = BuildPrompt(partType, topic, turns, metrics);
        var response = await _cli.RunAsync(
            prompt, ClaudeOutputFormat.Json, timeout: TimeSpan.FromMinutes(3), ct: ct);

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            _log.LogWarning("Speaking evaluator got empty/error response from Claude CLI.");
            return null;
        }

        return TryParseReport(response.Text);
    }

    private static string BuildPrompt(
        SpeakingPartType part, string? topic, IReadOnlyList<SpeakingTurn> turns, SpeakingMetrics m)
    {
        var partLabel = part switch
        {
            SpeakingPartType.Part1 => "Part 1 — Introduction & familiar topics (4–5 minutes)",
            SpeakingPartType.Part2 => "Part 2 — Long turn (1 minute prep + 2 minute monologue)",
            SpeakingPartType.Part3 => "Part 3 — Two-way discussion (4–5 minutes)",
            _ => "IELTS Speaking"
        };

        var sb = new StringBuilder();
        sb.AppendLine("You are a certified IELTS Speaking examiner. Score the user's responses strictly");
        sb.AppendLine("by the public band descriptors below. Respond with ONLY a single JSON object");
        sb.AppendLine("matching the schema in the rubric. No prose, no markdown fence, no comments.");
        sb.AppendLine("Note: pronunciation can only be inferred indirectly from transcription patterns;");
        sb.AppendLine("err on the conservative side (band 6.0 default) when uncertain.");
        sb.AppendLine();
        sb.AppendLine("===== RUBRIC =====");
        sb.AppendLine(RubricLoader.Speaking);
        sb.AppendLine();
        sb.AppendLine($"===== PART: {partLabel} =====");
        if (!string.IsNullOrWhiteSpace(topic))
        {
            sb.AppendLine($"Topic: {topic}");
        }
        sb.AppendLine();
        sb.AppendLine("===== SPEECH METRICS (computed locally) =====");
        sb.AppendLine($"Words per minute: {m.WordsPerMinute:0.0}");
        sb.AppendLine($"Pause ratio (silence / total): {m.PauseRatio:P0}");
        sb.AppendLine($"Filler words count: {m.FillerCount}");
        sb.AppendLine($"Type-token ratio (lexical diversity): {m.TypeTokenRatio:0.00}");
        sb.AppendLine();
        sb.AppendLine("===== USER RESPONSES =====");
        for (var i = 0; i < turns.Count; i++)
        {
            var t = turns[i];
            sb.AppendLine($"Q{i + 1}: {t.Question}");
            if (!string.IsNullOrWhiteSpace(t.ModelAnswer))
            {
                sb.AppendLine("Reference (band 7+ exemplar for calibration):");
                sb.AppendLine(t.ModelAnswer.Trim());
            }
            sb.AppendLine($"A{i + 1} (≈{t.DurationSeconds}s): {t.UserTranscript}");
            sb.AppendLine();
        }
        sb.AppendLine("Return JSON now.");
        return sb.ToString();
    }

    private SpeakingScoreReport? TryParseReport(string raw)
    {
        var trimmed = ExtractJsonObject(raw);
        if (trimmed is null) return null;
        try
        {
            return JsonSerializer.Deserialize<SpeakingScoreReport>(trimmed,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse SpeakingScoreReport: {Raw}", raw[..Math.Min(raw.Length, 300)]);
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
