namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class TestSet
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public IeltsSection Section { get; set; }
    public IeltsTestMode Mode { get; set; } = IeltsTestMode.Academic;
    public ContentSource Source { get; set; } = ContentSource.Unknown;
    public string? AuthorAttribution { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When true, this test must be run in strict exam mode only: mandatory timer, no training-mode
    /// option, no backward navigation between parts. Used for tests that mirror real exam material.
    /// </summary>
    public bool IsExamOnly { get; set; }

    public ICollection<TestPart> Parts { get; set; } = new List<TestPart>();
}
