namespace EnglishStudio.Modules.Ielts.Core.Entities;

/// <summary>
/// A group of consecutive questions in a <see cref="TestPart"/> that share common
/// metadata: instruction text, an options bank (List of Headings / Word box / List of phrases),
/// an image (map), and/or a pre-filled example.
/// </summary>
public class TestQuestionGroup
{
    public int Id { get; set; }
    public int TestPartId { get; set; }
    public TestPart TestPart { get; set; } = null!;

    public int OrderInPart { get; set; }

    /// <summary>Layout hint for the renderer (FlatList | SummaryFlow | MapLabeling).</summary>
    public QuestionGroupLayout Layout { get; set; } = QuestionGroupLayout.FlatList;

    /// <summary>Instruction shown above the questions (e.g. "Choose the appropriate letters A-D").</summary>
    public string? InstructionText { get; set; }

    /// <summary>
    /// JSON array of option strings shared across all questions in the group:
    /// list of headings (i-xiii), list of phrases (A-K), word box, list of places (A-E), etc.
    /// </summary>
    public string? SharedOptionsJson { get; set; }

    /// <summary>Localized title of the shared options block, e.g. "List of headings", "Word box".</summary>
    public string? SharedListTitle { get; set; }

    /// <summary>
    /// Relative path (under %AppData%\EnglishStudio\IeltsContent\Reading\&lt;test-code&gt;\) of the
    /// image attached to the group, e.g. "map.png" for MapLabeling layout. Resolved at runtime.
    /// </summary>
    public string? ImagePath { get; set; }

    /// <summary>Pre-filled example item, e.g. "Paragraph F" / "Yes" / "beware".</summary>
    public string? ExampleStem { get; set; }
    public string? ExampleAnswer { get; set; }

    /// <summary>
    /// For <see cref="QuestionGroupLayout.SummaryFlow"/>: a single block of text with
    /// numbered placeholders ("… been {19} by technology. Messages are {20} …"). Numbers
    /// reference child questions by their <see cref="TestQuestion.OrderInPart"/>'s display number.
    /// </summary>
    public string? SummaryTemplate { get; set; }

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
}
