using System.Text.Json;
using EnglishStudio.Modules.Ai;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Single generation call: prompt Claude CLI to produce an essay calibrated
/// to a target band. The response must be a JSON object with answer + examinerComment.
/// </summary>
public sealed class ClaudeBandGenerator
{
    private readonly IClaudeCliClient _cli;
    private readonly ILogger _log;

    public ClaudeBandGenerator(IClaudeCliClient cli, ILogger log)
    {
        _cli = cli;
        _log = log;
    }

    public async Task<GeneratedSample?> GenerateAsync(
        BandGap gap,
        string? regenHint,
        CancellationToken ct)
    {
        var prompt = PromptBuilder.BuildGenerationPrompt(gap, regenHint);
        var response = await _cli.RunAsync(
            prompt,
            ClaudeOutputFormat.Json,
            timeout: TimeSpan.FromMinutes(5),
            ct: ct);

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            _log.LogWarning("Generator: CLI returned error or empty body.");
            return null;
        }

        var json = JsonUtil.ExtractObject(response.Text);
        if (json is null)
        {
            _log.LogWarning("Generator: no JSON object in response (first 200 chars: {Sample})",
                response.Text[..Math.Min(response.Text.Length, 200)]);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var answer = root.TryGetProperty("answer", out var a) ? a.GetString() : null;
            var comment = root.TryGetProperty("examinerComment", out var c) ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(comment))
            {
                _log.LogWarning("Generator: missing 'answer' or 'examinerComment' in JSON.");
                return null;
            }
            return new GeneratedSample(answer!, comment!);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Generator: malformed JSON. First 300 chars: {Sample}",
                json[..Math.Min(json.Length, 300)]);
            return null;
        }
    }
}
