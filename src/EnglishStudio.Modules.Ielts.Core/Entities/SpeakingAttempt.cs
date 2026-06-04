namespace EnglishStudio.Modules.Ielts.Core.Entities;

public enum SpeakingAttemptMode
{
    FullMock = 1,
    Part1Only = 2,
    Part2Only = 3,
    Part3Only = 4
}

public class SpeakingAttempt
{
    public int Id { get; set; }
    public SpeakingAttemptMode Mode { get; set; }

    /// <summary>
    /// Topic / cue-card bank chosen for this attempt. For FullMock this is the Part 2 bank;
    /// for Part1Only it is the Part 1 bank when the user picked a fixed topic, otherwise null
    /// (mixed-topic random run). Always set for Part2Only / Part3Only.
    /// </summary>
    public int? TopicBankId { get; set; }
    public SpeakingQuestionBank? TopicBank { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public double? BandFluencyCoherence { get; set; }
    public double? BandLexicalResource { get; set; }
    public double? BandGrammar { get; set; }
    public double? BandPronunciation { get; set; }
    public double? BandOverall { get; set; }

    public string? FeedbackJson { get; set; }

    public ICollection<SpeakingResponse> Responses { get; set; } = new List<SpeakingResponse>();
}
