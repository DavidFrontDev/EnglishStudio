using EnglishStudio.Modules.Ielts.Writing.Seed;

namespace EnglishStudio.IeltsWritingBandGen;

/// <summary>
/// One missing (taskCode, targetBand) slot that the generator must fill.
/// Carries enough context (prompt + existing references) for the generator
/// to calibrate the new sample against what is already in the seed.
/// </summary>
public sealed record BandGap(
    string SetCode,
    string TaskCode,
    string Kind,
    string PromptText,
    int TargetBand,
    IReadOnlyList<ReferenceSample> Existing);

public sealed record ReferenceSample(int BandLevel, string AnswerText, string? ExaminerComment);

public sealed record GeneratedSample(
    string AnswerText,
    string ExaminerComment);

public sealed record ValidatorScore(
    double TaskAchievement,
    double CoherenceCohesion,
    double LexicalResource,
    double GrammaticalRangeAccuracy,
    double Overall);
