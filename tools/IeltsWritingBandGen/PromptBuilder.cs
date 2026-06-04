using System.Reflection;
using System.Text;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// Builds the two prompts the tool sends to Claude CLI:
///   1) Generator — produces an essay calibrated to a target band.
///   2) Validator — independently scores the produced essay so we can detect drift.
/// </summary>
public static class PromptBuilder
{
    private static string? _descriptors;
    public static string Descriptors => _descriptors ??= LoadEmbedded("BandDescriptors.md");

    public static string BuildGenerationPrompt(BandGap gap, string? regenHint = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ROLE: You are an IELTS Writing examiner generating a CALIBRATION SAMPLE.");
        sb.AppendLine($"Your task is to write an essay that an examiner would score EXACTLY band {gap.TargetBand}.0.");
        sb.AppendLine();
        sb.AppendLine($"TASK TYPE: {TaskTypeLabel(gap.Kind)}");
        sb.AppendLine();
        sb.AppendLine("TASK PROMPT:");
        sb.AppendLine(gap.PromptText);
        sb.AppendLine();

        if (gap.Existing.Count > 0)
        {
            sb.AppendLine("EXISTING REFERENCE(S) for this exact task — calibrate AGAINST these, do not duplicate:");
            foreach (var r in gap.Existing.OrderBy(x => x.BandLevel))
            {
                sb.AppendLine($"--- Band {r.BandLevel} reference ---");
                sb.AppendLine(r.AnswerText);
                if (!string.IsNullOrWhiteSpace(r.ExaminerComment))
                {
                    sb.AppendLine($"Examiner verdict on Band {r.BandLevel}: {r.ExaminerComment}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine("BAND DESCRIPTORS (apply STRICTLY to band " + gap.TargetBand + "):");
        sb.AppendLine(Descriptors);
        sb.AppendLine();

        sb.AppendLine("INSTRUCTIONS:");
        sb.AppendLine($"- Write an essay scoring EXACTLY band {gap.TargetBand}.0 by an IELTS examiner.");
        sb.AppendLine("- The essay must read as plausibly hand-written by a real candidate at that level —");
        sb.AppendLine("  including authentic markers from the descriptors above. Do NOT produce a polished");
        sb.AppendLine("  essay and then \"break\" it; write naturally at the target level.");
        sb.AppendLine(WordCountHint(gap.Kind));
        sb.AppendLine("- The examinerComment must justify the band via TR/CC/LR/GRA in 2–4 sentences.");
        if (!string.IsNullOrWhiteSpace(regenHint))
        {
            sb.AppendLine();
            sb.AppendLine("PREVIOUS ATTEMPT FEEDBACK:");
            sb.AppendLine(regenHint);
        }
        sb.AppendLine();
        sb.AppendLine("OUTPUT: a single JSON object, no markdown fence, no prose around it:");
        sb.AppendLine("""{"answer":"<the essay text, \n for paragraph breaks>","examinerComment":"<2-4 sentences>"}""");
        return sb.ToString();
    }

    public static string BuildValidationPrompt(BandGap gap, string generatedAnswer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ROLE: You are a certified IELTS Writing examiner. Score this essay STRICTLY by the");
        sb.AppendLine("public band descriptors on TR/CC/LR/GRA. Do not be generous. Half-bands allowed.");
        sb.AppendLine();
        sb.AppendLine($"TASK TYPE: {TaskTypeLabel(gap.Kind)}");
        sb.AppendLine();
        sb.AppendLine("TASK PROMPT:");
        sb.AppendLine(gap.PromptText);
        sb.AppendLine();
        sb.AppendLine("ESSAY:");
        sb.AppendLine(generatedAnswer);
        sb.AppendLine();
        sb.AppendLine("Output ONLY a JSON object, no prose, no markdown fence:");
        sb.AppendLine("""{"ta":X.X,"cc":X.X,"lr":X.X,"gra":X.X,"overall":X.X}""");
        return sb.ToString();
    }

    private static string TaskTypeLabel(string kind) => kind switch
    {
        "Task1Academic" => "Academic Writing Task 1 — ≥150 words, 20 minutes — describe visual data",
        "Task1GeneralTraining" => "General Training Writing Task 1 — letter, ≥150 words, 20 minutes",
        "Task2" => "Writing Task 2 — essay, ≥250 words, 40 minutes",
        _ => "IELTS Writing task"
    };

    private static string WordCountHint(string kind) => kind == "Task2"
        ? "- Word count: ≥250 (Task 2). Bands 5–6 usually 230–280 words, bands 7–9 usually 270–320."
        : "- Word count: ≥150 (Task 1). Bands 5–6 usually 145–180 words, bands 7–9 usually 170–210.";

    private static string LoadEmbedded(string name)
    {
        var asm = typeof(PromptBuilder).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Embedded resource '{name}' not found.");
        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
