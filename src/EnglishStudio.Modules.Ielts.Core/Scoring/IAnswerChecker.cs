using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

public interface IAnswerChecker
{
    /// <summary>
    /// Returns true if this checker handles the given question type.
    /// </summary>
    bool CanHandle(QuestionType type);

    /// <summary>
    /// Check the user's answer against the question's answer key.
    /// </summary>
    AnswerCheckResult Check(TestQuestion question, string userAnswerJson);
}

public readonly record struct AnswerCheckResult(bool IsCorrect, int PointsEarned, string? Note = null);
