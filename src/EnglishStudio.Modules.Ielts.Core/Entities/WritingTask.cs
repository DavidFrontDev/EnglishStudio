namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class WritingTask
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public WritingTaskKind Kind { get; set; }
    public string PromptText { get; set; } = string.Empty;
    public string? ChartSpecJson { get; set; }
    public string? ImagePath { get; set; }
    public int MinWords { get; set; }
    public int RecommendedMinutes { get; set; }
    public WritingChartType ChartType { get; set; } = WritingChartType.None;
    public string? TopicCategory { get; set; }
    public ContentSource Source { get; set; } = ContentSource.Unknown;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this task is part of a full IELTS Writing test (Task 1 + Task 2), points to the containing
    /// <see cref="TestSet"/>. Null for standalone training drills.
    /// </summary>
    public int? TestSetId { get; set; }
    public TestSet? TestSet { get; set; }

    /// <summary>1 = Task 1, 2 = Task 2 within a TestSet. 0 for standalone drills.</summary>
    public int OrderInSet { get; set; }

    public ICollection<WritingModelAnswer> ModelAnswers { get; set; } = new List<WritingModelAnswer>();
}
