namespace EnglishStudio.Modules.Reading.Services;

public enum ComprehensionKind
{
    MultipleChoice,
    Open
}

/// <summary>
/// A comprehension question for a text. For <see cref="ComprehensionKind.Open"/> questions
/// <see cref="Options"/> is empty and <see cref="CorrectOptionIndex"/> is -1.
/// </summary>
public sealed record ComprehensionQuestionDto(
    int Id,
    ComprehensionKind Kind,
    string Prompt,
    IReadOnlyList<string> Options,
    int CorrectOptionIndex);

/// <summary>Grading of one answer (MCQ checked locally; Open graded by Claude).</summary>
public sealed record ComprehensionVerdictDto(
    bool IsCorrect,
    double Score,
    string FeedbackRu);
