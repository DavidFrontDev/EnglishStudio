using EnglishStudio.Modules.Reading.Services;

namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// One comprehension question generated for a <see cref="ReadingText"/> (F2). Generated once via
/// Claude and cached, so a text's questions are stable across reads.
/// </summary>
public class ComprehensionQuestion
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }

    public ComprehensionKind Kind { get; set; }

    public string Prompt { get; set; } = string.Empty;

    /// <summary>JSON array of options for <see cref="ComprehensionKind.MultipleChoice"/>; null for Open.</summary>
    public string? OptionsJson { get; set; }

    /// <summary>Index of the correct option for MCQ; -1 for Open.</summary>
    public int CorrectOptionIndex { get; set; } = -1;

    /// <summary>Reference answer for Open questions (used to grade via Claude).</summary>
    public string? ModelAnswer { get; set; }

    /// <summary>Display order within the text's question set.</summary>
    public int OrderIndex { get; set; }
}
