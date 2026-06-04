namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class WritingModelAnswer
{
    public int Id { get; set; }
    public int WritingTaskId { get; set; }
    public WritingTask WritingTask { get; set; } = null!;

    public int BandLevel { get; set; }
    public string AnswerText { get; set; } = string.Empty;
    public string? AnnotationJson { get; set; }

    /// <summary>
    /// Examiner's written commentary on this candidate response (why it received the band it did,
    /// what strengths/weaknesses are present). Used as a calibration anchor when an LLM
    /// evaluates a user's essay for the same task.
    /// </summary>
    public string? ExaminerComment { get; set; }
}
