namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class TestPart
{
    public int Id { get; set; }
    public int TestSetId { get; set; }
    public TestSet TestSet { get; set; } = null!;

    public int OrderInTest { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>Passage text (Reading), transcript (Listening), writing prompt (Writing). Null for Speaking.</summary>
    public string? BodyText { get; set; }
    public string? IntroNoteRu { get; set; }

    /// <summary>Path under %AppData%\EnglishStudio\IeltsContent\ — present for Listening sections.</summary>
    public string? AudioPath { get; set; }

    /// <summary>Audio transcript for this part (Listening) — shown as a study aid in training mode only.</summary>
    public string? Transcript { get; set; }

    /// <summary>Path under %AppData%\EnglishStudio\IeltsContent\ — present for Writing Task 1 (charts/maps).</summary>
    public string? ImagePath { get; set; }

    public ICollection<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
    public ICollection<TestQuestionGroup> Groups { get; set; } = new List<TestQuestionGroup>();
}
