using System.Text.Json;
using EnglishStudio.Modules.Ai.Evaluators;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Core.Data;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Ielts.Speaking;

public sealed class SpeakingFeedbackService : ISpeakingFeedbackService
{
    private readonly IDbContextFactory<IeltsDbContext> _dbFactory;
    private readonly IIeltsSpeakingEvaluator _evaluator;
    private readonly ILogger<SpeakingFeedbackService> _log;

    public SpeakingFeedbackService(
        IDbContextFactory<IeltsDbContext> dbFactory,
        IIeltsSpeakingEvaluator evaluator,
        ILogger<SpeakingFeedbackService> log)
    {
        _dbFactory = dbFactory;
        _evaluator = evaluator;
        _log = log;
    }

    public async Task<SpeakingScoreReport?> EvaluateAndSaveAsync(int attemptId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var attempt = await db.SpeakingAttempts
            .Include(a => a.Responses.OrderBy(r => r.OrderInAttempt))
                .ThenInclude(r => r.Question)
                    .ThenInclude(q => q.Bank)
            .FirstOrDefaultAsync(a => a.Id == attemptId, ct);

        if (attempt is null)
        {
            _log.LogWarning("SpeakingFeedbackService: attempt {Id} not found.", attemptId);
            return null;
        }
        if (attempt.Responses.Count == 0)
        {
            _log.LogWarning("SpeakingFeedbackService: attempt {Id} has no responses to grade.", attemptId);
            return null;
        }

        // Group responses by Part, derived from the bank each question belongs to.
        var grouped = attempt.Responses
            .GroupBy(r => r.Question.Bank.Part)
            .OrderBy(g => g.Key)
            .ToList();

        var partReports = new List<(SpeakingBankPart Part, SpeakingScoreReport Report)>();

        foreach (var group in grouped)
        {
            var turns = group
                .OrderBy(r => r.OrderInAttempt)
                .Select(r => new SpeakingTurn(
                    Question: r.Question.Text,
                    UserTranscript: r.Transcript ?? string.Empty,
                    DurationSeconds: r.DurationSeconds,
                    ModelAnswer: r.Question.ModelAnswer))
                .ToList();

            var partMetrics = AggregateMetrics(group);
            var topic = group.Select(r => r.Question.Bank.TopicLabel).FirstOrDefault();
            var partType = ToAiPart(group.Key);

            try
            {
                var report = await _evaluator.EvaluateAsync(partType, topic, turns, partMetrics, ct);
                if (report is not null)
                {
                    partReports.Add((group.Key, report));
                }
                else
                {
                    _log.LogWarning("Speaking evaluator returned null for attempt {Id} {Part}", attemptId, group.Key);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Speaking evaluator threw for attempt {Id} {Part}", attemptId, group.Key);
            }
        }

        if (partReports.Count == 0) return null;

        // Aggregate Part-level reports into a single attempt-level scorecard. Average the four
        // criterion bands across the parts the user completed; overall = official IELTS .5 rounding.
        var avgFC = partReports.Average(p => p.Report.FluencyCoherence);
        var avgLR = partReports.Average(p => p.Report.LexicalResource);
        var avgGR = partReports.Average(p => p.Report.GrammaticalRangeAccuracy);
        var avgPR = partReports.Average(p => p.Report.Pronunciation);
        var overall = IeltsRound((avgFC + avgLR + avgGR + avgPR) / 4.0);

        attempt.BandFluencyCoherence = Round05(avgFC);
        attempt.BandLexicalResource = Round05(avgLR);
        attempt.BandGrammar = Round05(avgGR);
        attempt.BandPronunciation = Round05(avgPR);
        attempt.BandOverall = overall;

        // Combined feedback packet — per-part raw reports plus the aggregate. The UI can show
        // a tabbed breakdown without us inventing a new DTO contract.
        var packet = new
        {
            attemptId,
            overall = new
            {
                fluencyCoherence = attempt.BandFluencyCoherence,
                lexicalResource = attempt.BandLexicalResource,
                grammaticalRangeAccuracy = attempt.BandGrammar,
                pronunciation = attempt.BandPronunciation,
                band = overall
            },
            parts = partReports.Select(p => new
            {
                part = p.Part.ToString(),
                report = p.Report
            })
        };
        attempt.FeedbackJson = JsonSerializer.Serialize(packet);

        await db.SaveChangesAsync(ct);

        // Return the aggregate as a synthesised SpeakingScoreReport — useful for hub VMs that
        // just want a single object back without parsing the packet.
        var combinedFeedbackEn = string.Join("\n\n",
            partReports.Select(p => $"[{p.Part}] {p.Report.FeedbackEn}"));
        var combinedFeedbackRu = string.Join("\n\n",
            partReports.Select(p => $"[{p.Part}] {p.Report.FeedbackRu}"));
        var combinedStrengths = partReports.SelectMany(p => p.Report.Strengths).Distinct().Take(6).ToList();
        var combinedImprovements = partReports.SelectMany(p => p.Report.Improvements).Distinct().Take(6).ToList();

        return new SpeakingScoreReport(
            FluencyCoherence: attempt.BandFluencyCoherence.Value,
            LexicalResource: attempt.BandLexicalResource.Value,
            GrammaticalRangeAccuracy: attempt.BandGrammar.Value,
            Pronunciation: attempt.BandPronunciation.Value,
            Overall: overall,
            FeedbackEn: combinedFeedbackEn,
            FeedbackRu: combinedFeedbackRu)
        {
            Strengths = combinedStrengths,
            Improvements = combinedImprovements
        };
    }

    private static SpeakingMetrics AggregateMetrics(IEnumerable<SpeakingResponse> responses)
    {
        var list = responses.ToList();
        if (list.Count == 0) return new SpeakingMetrics(0, 0, 0, 0);

        // Duration-weighted mean for WPM and pause ratio so a long Part 2 monologue doesn't
        // get drowned out by short Part 1 answers (or vice versa).
        var totalDur = list.Sum(r => Math.Max(1, r.DurationSeconds));
        var wpm = list.Sum(r => (r.WpmRate ?? 0) * Math.Max(1, r.DurationSeconds)) / totalDur;
        var pause = list.Sum(r => (r.PauseRatio ?? 0) * Math.Max(1, r.DurationSeconds)) / totalDur;
        var ttr = list.Average(r => r.TypeTokenRatio ?? 0);
        var fillers = list.Sum(r => r.FillerCount ?? 0);
        return new SpeakingMetrics(wpm, pause, fillers, ttr);
    }

    private static SpeakingPartType ToAiPart(SpeakingBankPart part) => part switch
    {
        SpeakingBankPart.Part1 => SpeakingPartType.Part1,
        SpeakingBankPart.Part2 => SpeakingPartType.Part2,
        SpeakingBankPart.Part3 => SpeakingPartType.Part3,
        _ => SpeakingPartType.Part1
    };

    /// <summary>Half-band rounding for the 0.0 / 0.5 grid the four criteria live on.</summary>
    private static double Round05(double v) => Math.Round(v * 2, MidpointRounding.AwayFromZero) / 2.0;

    /// <summary>
    /// Official IELTS overall rounding: arithmetic mean of the four criterion bands rounded to
    /// the nearest half band, with exact .25/.75 midpoints rounding up.
    /// </summary>
    private static double IeltsRound(double mean) => OverallBandCalculator.RoundToOfficialBand(mean);
}
