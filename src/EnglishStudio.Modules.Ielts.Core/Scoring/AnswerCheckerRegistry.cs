using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.Modules.Ielts.Core.Scoring;

/// <summary>
/// Routes a <see cref="TestQuestion"/> to the first <see cref="IAnswerChecker"/> that handles its type.
/// </summary>
public sealed class AnswerCheckerRegistry
{
    private readonly IReadOnlyList<IAnswerChecker> _checkers;

    public AnswerCheckerRegistry(IEnumerable<IAnswerChecker> checkers)
    {
        _checkers = checkers.ToList();
    }

    public AnswerCheckResult Check(TestQuestion question, string userAnswerJson)
    {
        // Map/Diagram labeling has two variants: pick-from-list (matching checker) and
        // write-words-from-passage (text-completion semantics). Route the text variant
        // to TextAnswerChecker so the NMTW limit and synonym matching apply.
        if ((question.Type == QuestionType.DiagramLabeling || question.Type == QuestionType.MapLabeling)
            && string.IsNullOrWhiteSpace(question.OptionsJson)
            && string.IsNullOrWhiteSpace(question.Group?.SharedOptionsJson))
        {
            var text = _checkers.OfType<TextAnswerChecker>().FirstOrDefault();
            if (text is not null) return text.Check(question, userAnswerJson);
        }

        foreach (var checker in _checkers)
        {
            if (checker.CanHandle(question.Type))
            {
                return checker.Check(question, userAnswerJson);
            }
        }
        throw new InvalidOperationException(
            $"No IAnswerChecker registered for question type {question.Type}.");
    }
}
