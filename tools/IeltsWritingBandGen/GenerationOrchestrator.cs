using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Loops over BandGaps, calls generator → validator, retries on drift, persists each
/// accepted sample as a per-gap JSON file under output/. Resume-friendly: skips any
/// gap whose output file already exists.
/// </summary>
public sealed class GenerationOrchestrator
{
    private const double AcceptableDrift = 0.5;
    private const int MaxAttempts = 3;

    private readonly ClaudeBandGenerator _generator;
    private readonly ClaudeBandValidator _validator;
    private readonly ILogger _log;
    private readonly string _outputDir;
    private readonly bool _skipValidator;

    public GenerationOrchestrator(
        ClaudeBandGenerator generator,
        ClaudeBandValidator validator,
        ILogger log,
        string outputDir,
        bool skipValidator)
    {
        _generator = generator;
        _validator = validator;
        _log = log;
        _outputDir = outputDir;
        _skipValidator = skipValidator;
        Directory.CreateDirectory(_outputDir);
    }

    public static string FileNameFor(BandGap gap) =>
        $"{gap.TaskCode}-band{gap.TargetBand:00}.json";

    public string FilePathFor(BandGap gap) =>
        Path.Combine(_outputDir, FileNameFor(gap));

    public async Task<int> RunAsync(IReadOnlyList<BandGap> gaps, CancellationToken ct)
    {
        var produced = 0;
        var skipped = 0;
        var failed = new List<string>();

        for (var i = 0; i < gaps.Count; i++)
        {
            var gap = gaps[i];
            var dest = FilePathFor(gap);
            if (File.Exists(dest))
            {
                skipped++;
                continue;
            }

            _log.LogInformation("──── [{Idx}/{Total}] {Code} band {Band} ────",
                i + 1, gaps.Count, gap.TaskCode, gap.TargetBand);

            var accepted = await TryGenerateAcceptableAsync(gap, ct);
            if (accepted is null)
            {
                failed.Add($"{gap.TaskCode}-band{gap.TargetBand}");
                continue;
            }

            await PersistAsync(gap, accepted.Value.sample, accepted.Value.score, dest, ct);
            produced++;
        }

        _log.LogInformation("════ Generation done. Produced {Ok}, skipped (resume) {Skip}, failed {Fail}.",
            produced, skipped, failed.Count);
        if (failed.Count > 0)
        {
            _log.LogWarning("Failed: {List}", string.Join(", ", failed));
        }
        return failed.Count;
    }

    private async Task<(GeneratedSample sample, ValidatorScore? score)?> TryGenerateAcceptableAsync(
        BandGap gap, CancellationToken ct)
    {
        string? hint = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            _log.LogInformation("Attempt {N}/{M}: generating…", attempt, MaxAttempts);
            GeneratedSample? sample;
            try
            {
                sample = await _generator.GenerateAsync(gap, hint, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Generator threw, will retry.");
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                continue;
            }
            if (sample is null)
            {
                hint = "Your previous response could not be parsed. Output a single JSON object only.";
                continue;
            }

            if (_skipValidator)
            {
                _log.LogInformation("Validator skipped (--no-validate). Accepting.");
                return (sample, null);
            }

            ValidatorScore? score;
            try
            {
                score = await _validator.ValidateAsync(gap, sample.AnswerText, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Validator threw, accepting without score.");
                return (sample, null);
            }

            if (score is null)
            {
                _log.LogWarning("Validator returned no score; accepting without score.");
                return (sample, null);
            }

            var drift = Math.Abs(score.Overall - gap.TargetBand);
            _log.LogInformation("Validator: overall {Overall} (TR {TR} CC {CC} LR {LR} GRA {GRA}) — drift {Drift}",
                score.Overall, score.TaskAchievement, score.CoherenceCohesion,
                score.LexicalResource, score.GrammaticalRangeAccuracy, drift);

            if (drift <= AcceptableDrift)
            {
                return (sample, score);
            }

            hint = $"Previous attempt was scored {score.Overall:F1} by the validator, " +
                   $"but the target is exactly {gap.TargetBand}.0. " +
                   (score.Overall > gap.TargetBand
                       ? "Make the essay weaker: introduce more grammar errors, simpler vocabulary, less coherence, and less developed ideas."
                       : "Make the essay stronger: use richer vocabulary, more complex grammar with fewer errors, and better-developed ideas.");
        }

        _log.LogError("✗ {Code} band {Band} failed after {N} attempts.",
            gap.TaskCode, gap.TargetBand, MaxAttempts);
        return null;
    }

    private async Task PersistAsync(
        BandGap gap, GeneratedSample sample, ValidatorScore? score, string dest, CancellationToken ct)
    {
        var payload = new
        {
            taskCode = gap.TaskCode,
            setCode = gap.SetCode,
            kind = gap.Kind,
            targetBand = gap.TargetBand,
            validatorScore = score,
            answer = sample.AnswerText,
            examinerComment = sample.ExaminerComment,
            generatedAt = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await File.WriteAllTextAsync(dest, json, ct);
        _log.LogInformation("✓ saved {File}", Path.GetFileName(dest));
    }
}
