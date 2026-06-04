namespace EnglishStudio.Modules.Dictionary.Entities;

public enum PronunciationCategory
{
    Unrecognized = 0,
    Poor = 1,
    Good = 2,
    Excellent = 3,
}

public class PronunciationAttempt
{
    public int Id { get; set; }

    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    public string TargetText { get; set; } = string.Empty;
    public string RecognizedText { get; set; } = string.Empty;

    public int Score { get; set; }
    public PronunciationCategory Category { get; set; }

    public DateTime RecordedAt { get; set; }
}
