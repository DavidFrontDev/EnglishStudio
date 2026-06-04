namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class WritingAttempt
{
    public int Id { get; set; }
    public int WritingTaskId { get; set; }
    public WritingTask WritingTask { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public int WordCount { get; set; }
    public int DurationSeconds { get; set; }
    public string UserText { get; set; } = string.Empty;

    public double? BandTaskAchievement { get; set; }
    public double? BandCoherence { get; set; }
    public double? BandLexical { get; set; }
    public double? BandGrammar { get; set; }
    public double? BandOverall { get; set; }
    public string? FeedbackJson { get; set; }
}
