namespace EnglishStudio.Modules.Ielts.Writing.Seed;

public sealed class WritingTestSetDto
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Attribution { get; set; }
    public WritingTaskDto Task1 { get; set; } = new();
    public WritingTaskDto Task2 { get; set; } = new();
}

public sealed class WritingTaskDto
{
    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string PromptText { get; set; } = string.Empty;
    public string? ChartSpecJson { get; set; }
    public string? ImageFile { get; set; }
    public int MinWords { get; set; }
    public int RecommendedMinutes { get; set; }
    public string? ChartType { get; set; }
    public string? TopicCategory { get; set; }
    public List<WritingModelAnswerDto> ModelAnswers { get; set; } = new();
}

public sealed class WritingModelAnswerDto
{
    public int BandLevel { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public string? AnnotationJson { get; set; }
    public string? ExaminerComment { get; set; }
}
