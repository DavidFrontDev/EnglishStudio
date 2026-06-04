namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class SpeakingResponse
{
    public int Id { get; set; }
    public int SpeakingAttemptId { get; set; }
    public SpeakingAttempt Attempt { get; set; } = null!;
    public int SpeakingQuestionId { get; set; }
    public SpeakingQuestion Question { get; set; } = null!;

    public int OrderInAttempt { get; set; }

    /// <summary>Absolute path under %AppData%/EnglishStudio/Speaking/Audio/{attemptId}/{order}.wav.</summary>
    public string AudioPath { get; set; } = string.Empty;
    public string? Transcript { get; set; }
    public int DurationSeconds { get; set; }

    public double? WpmRate { get; set; }
    public double? PauseRatio { get; set; }
    public int? FillerCount { get; set; }
    public double? TypeTokenRatio { get; set; }
}
