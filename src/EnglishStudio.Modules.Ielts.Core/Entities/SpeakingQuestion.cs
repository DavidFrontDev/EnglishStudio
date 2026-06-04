namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class SpeakingQuestion
{
    public int Id { get; set; }
    public int BankId { get; set; }
    public SpeakingQuestionBank Bank { get; set; } = null!;
    public int OrderInBank { get; set; }
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// For Part 3 questions: pointer to the Part 2 cue-card question that this follow-up
    /// extends. Null for Part 1 / standalone questions.
    /// </summary>
    public int? FollowUpToQuestionId { get; set; }
    public SpeakingQuestion? FollowUpToQuestion { get; set; }

    /// <summary>
    /// Optional reference answer / band-7+ exemplar used to calibrate AI evaluation.
    /// Null until a model answer has been authored or synthesised for this question.
    /// </summary>
    public string? ModelAnswer { get; set; }
}
