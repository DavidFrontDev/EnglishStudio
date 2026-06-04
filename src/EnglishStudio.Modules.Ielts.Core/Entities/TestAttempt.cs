namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class TestAttempt
{
    public int Id { get; set; }
    public int TestSetId { get; set; }
    public TestSet TestSet { get; set; } = null!;

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int DurationSeconds { get; set; }

    public int RawScore { get; set; }
    public double BandEstimate { get; set; }

    /// <summary>When true, exam constraints (single play, timer, etc.) are relaxed for learning.</summary>
    public bool IsTrainingMode { get; set; }

    public bool IsCompleted => FinishedAt.HasValue;

    /// <summary>
    /// Optional AI-generated diagnostic for this attempt (mirrors WritingAttempt.FeedbackJson).
    /// Format depends on the section: for Listening it's a serialised
    /// <c>EnglishStudio.Modules.Ai.Reports.ListeningScoreReport</c>; null until the user
    /// requests an AI review on the result screen.
    /// </summary>
    public string? FeedbackJson { get; set; }

    public ICollection<TestAnswer> Answers { get; set; } = new List<TestAnswer>();
}
