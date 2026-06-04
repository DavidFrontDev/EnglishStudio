using System.Text.Json;
using EnglishStudio.Modules.Ai;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Independent second CLI call that scores the generated essay on TR/CC/LR/GRA.
/// We use it to detect drift from the target band before accepting the sample.
/// </summary>
public sealed class ClaudeBandValidator
{
    private readonly IClaudeCliClient _cli;
    private readonly ILogger _log;

    public ClaudeBandValidator(IClaudeCliClient cli, ILogger log)
    {
        _cli = cli;
        _log = log;
    }

    public async Task<ValidatorScore?> ValidateAsync(
        BandGap gap,
        string generatedAnswer,
        CancellationToken ct)
    {
        var prompt = PromptBuilder.BuildValidationPrompt(gap, generatedAnswer);
        var response = await _cli.RunAsync(
            prompt,
            ClaudeOutputFormat.Json,
            timeout: TimeSpan.FromMinutes(3),
            ct: ct);

        if (response.IsError || string.IsNullOrWhiteSpace(response.Text))
        {
            _log.LogWarning("Validator: CLI returned error or empty body.");
            return null;
        }

        var json = JsonUtil.ExtractObject(response.Text);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new ValidatorScore(
                TaskAchievement: GetDouble(root, "ta"),
                CoherenceCohesion: GetDouble(root, "cc"),
                LexicalResource: GetDouble(root, "lr"),
                GrammaticalRangeAccuracy: GetDouble(root, "gra"),
                Overall: GetDouble(root, "overall"));
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Validator: malformed JSON. First 300 chars: {Sample}",
                json[..Math.Min(json.Length, 300)]);
            return null;
        }
    }

    private static double GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : 0;
}
