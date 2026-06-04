namespace EnglishStudio.Modules.Ielts.Core.Entities;

public enum SpeakingBankPart
{
    Part1 = 1,
    Part2 = 2,
    Part3 = 3
}

/// <summary>
/// A pool of questions on one topic for one Speaking Part. Part 2 banks store the cue
/// card prompt + bullet sub-points; Part 3 banks reference the matching Part 2 bank via
/// <see cref="LinkedPart2BankId"/> so the orchestrator can pair them in full mocks.
/// </summary>
public class SpeakingQuestionBank
{
    public int Id { get; set; }
    public SpeakingBankPart Part { get; set; }
    public string TopicCode { get; set; } = string.Empty;
    public string TopicLabel { get; set; } = string.Empty;
    public string? CueCardPrompt { get; set; }
    public string? CueCardSubpointsJson { get; set; }
    public int? LinkedPart2BankId { get; set; }
    public SpeakingQuestionBank? LinkedPart2Bank { get; set; }

    public ICollection<SpeakingQuestion> Questions { get; set; } = new List<SpeakingQuestion>();
}
