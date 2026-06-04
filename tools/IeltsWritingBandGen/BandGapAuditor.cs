using System.Text.Json;
using EnglishStudio.Modules.Ielts.Writing.Seed;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Reads writing_tests.json and produces a flat list of (taskCode, targetBand) gaps
/// to be filled by the generator. A "gap" is a band in {5,6,7,8,9} that is not yet
/// present among the task's modelAnswers.
/// </summary>
public static class BandGapAuditor
{
    public static readonly int[] RequiredBands = { 5, 6, 7, 8, 9 };

    public static List<WritingTestSetDto> LoadSeed(string seedPath)
    {
        using var stream = File.OpenRead(seedPath);
        var sets = JsonSerializer.Deserialize<List<WritingTestSetDto>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return sets ?? new List<WritingTestSetDto>();
    }

    public static List<BandGap> ComputeGaps(IReadOnlyList<WritingTestSetDto> sets)
    {
        var gaps = new List<BandGap>();
        foreach (var set in sets)
        {
            foreach (var task in new[] { set.Task1, set.Task2 })
            {
                // BandLevel is already int — fractional bands (5.5 → 6) are stored rounded.
                var occupied = task.ModelAnswers
                    .Where(ma => ma.BandLevel > 0)
                    .Select(ma => ma.BandLevel)
                    .ToHashSet();

                var existingRefs = task.ModelAnswers
                    .Where(ma => ma.BandLevel > 0 && !string.IsNullOrWhiteSpace(ma.AnswerText))
                    .Select(ma => new ReferenceSample(
                        ma.BandLevel,
                        ma.AnswerText,
                        ma.ExaminerComment))
                    .ToList();

                foreach (var band in RequiredBands)
                {
                    if (occupied.Contains(band)) continue;
                    gaps.Add(new BandGap(
                        SetCode: set.Code,
                        TaskCode: task.Code,
                        Kind: task.Kind,
                        PromptText: task.PromptText,
                        TargetBand: band,
                        Existing: existingRefs));
                }
            }
        }
        return gaps;
    }

    public static void WriteGapsReport(IReadOnlyList<BandGap> gaps, string outPath)
    {
        var summary = gaps
            .GroupBy(g => g.TaskCode)
            .Select(g => new
            {
                taskCode = g.Key,
                missingBands = g.Select(x => x.TargetBand).OrderBy(x => x).ToArray()
            })
            .OrderBy(x => x.taskCode)
            .ToList();

        var json = JsonSerializer.Serialize(new
        {
            totalGaps = gaps.Count,
            byTask = summary
        }, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(outPath, json);
    }
}
