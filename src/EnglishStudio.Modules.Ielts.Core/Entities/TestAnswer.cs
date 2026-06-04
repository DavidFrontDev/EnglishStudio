namespace EnglishStudio.Modules.Ielts.Core.Entities;

public class TestAnswer
{
    public int Id { get; set; }
    public int TestAttemptId { get; set; }
    public TestAttempt TestAttempt { get; set; } = null!;

    public int TestQuestionId { get; set; }
    public TestQuestion TestQuestion { get; set; } = null!;

    /// <summary>User's submitted answer (string or JSON depending on question type).</summary>
    public string UserAnswerJson { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
    public int PointsEarned { get; set; }
}
