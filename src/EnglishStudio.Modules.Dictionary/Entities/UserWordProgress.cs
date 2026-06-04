namespace EnglishStudio.Modules.Dictionary.Entities;

public enum SrsState
{
    New = 0,
    Learning = 1,
    Review = 2,
    Relearning = 3,
}

public enum SrsRating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4,
}

public class UserWordProgress
{
    public int Id { get; set; }

    // Polymorphic owner: exactly one of WordId / PhrasalVerbId / CollocationId is set.
    public int? WordId { get; set; }
    public Word? Word { get; set; }

    public int? PhrasalVerbId { get; set; }
    public PhrasalVerb? PhrasalVerb { get; set; }

    public int? CollocationId { get; set; }
    public Collocation? Collocation { get; set; }

    public SrsState State { get; set; } = SrsState.New;

    public double Stability { get; set; }
    public double Difficulty { get; set; }

    public DateTime? LastReviewedAt { get; set; }
    public DateTime? NextReviewAt { get; set; }

    public int ReviewCount { get; set; }
    public int LapseCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ReviewLog> ReviewLogs { get; set; } = new List<ReviewLog>();
}

public class ReviewLog
{
    public int Id { get; set; }

    public int UserWordProgressId { get; set; }
    public UserWordProgress UserWordProgress { get; set; } = null!;

    public DateTime ReviewedAt { get; set; }
    public SrsRating Rating { get; set; }

    public SrsState StateBefore { get; set; }
    public SrsState StateAfter { get; set; }
    public double StabilityBefore { get; set; }
    public double StabilityAfter { get; set; }
    public double DifficultyBefore { get; set; }
    public double DifficultyAfter { get; set; }

    public double ElapsedDays { get; set; }
    public double ScheduledIntervalDays { get; set; }
}
