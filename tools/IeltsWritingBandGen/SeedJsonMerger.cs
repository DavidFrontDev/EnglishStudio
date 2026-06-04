using System.Text.Json;
using EnglishStudio.Modules.Ielts.Writing.Seed;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Merges all per-gap JSON files in output/ back into writing_tests.json. Idempotent:
/// re-running with no new files makes no changes; running with new files appends them
/// to the task's modelAnswers (sorted by bandLevel). Never deletes existing entries.
/// </summary>
public static class SeedJsonMerger
{
    public sealed record MergeResult(int Added, int SkippedExisting, int OrphanFiles);

    public static MergeResult Merge(string seedPath, string outputDir)
    {
        var seed = BandGapAuditor.LoadSeed(seedPath);

        // Index tasks by code for O(1) lookup.
        var taskIndex = new Dictionary<string, WritingTaskDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in seed)
        {
            taskIndex[set.Task1.Code] = set.Task1;
            taskIndex[set.Task2.Code] = set.Task2;
        }

        var added = 0;
        var skipped = 0;
        var orphan = 0;

        foreach (var file in Directory.EnumerateFiles(outputDir, "acad-w-*-band*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var taskCode = root.GetProperty("taskCode").GetString()!;
            var band = root.GetProperty("targetBand").GetInt32();
            var answer = root.GetProperty("answer").GetString()!;
            var comment = root.GetProperty("examinerComment").GetString();

            if (!taskIndex.TryGetValue(taskCode, out var task))
            {
                orphan++;
                continue;
            }

            // Already merged? Skip — never overwrite existing reference essays.
            if (task.ModelAnswers.Any(ma => ma.BandLevel == band))
            {
                skipped++;
                continue;
            }

            task.ModelAnswers.Add(new WritingModelAnswerDto
            {
                BandLevel = band,
                AnswerText = answer,
                ExaminerComment = comment
            });
            added++;
        }

        // Sort each task's ModelAnswers by band ascending for predictable JSON diffs.
        foreach (var task in taskIndex.Values)
        {
            var sorted = task.ModelAnswers.OrderBy(ma => ma.BandLevel).ToList();
            task.ModelAnswers.Clear();
            foreach (var ma in sorted) task.ModelAnswers.Add(ma);
        }

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        var output = JsonSerializer.Serialize(seed, options);
        File.WriteAllText(seedPath, output);

        return new MergeResult(added, skipped, orphan);
    }
}
