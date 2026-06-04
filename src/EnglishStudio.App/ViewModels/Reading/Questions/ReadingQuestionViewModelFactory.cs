using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

internal static class ReadingQuestionViewModelFactory
{
    public static IReadingQuestionViewModel Create(TestQuestion source, int displayNumber, IReadOnlyList<string>? sharedOptions = null)
    {
        return source.Type switch
        {
            QuestionType.TrueFalseNotGiven or QuestionType.YesNoNotGiven
                => new TfngQuestionViewModel(source, displayNumber),

            QuestionType.MultipleChoiceSingle
                => new ChoiceSingleQuestionViewModel(source, displayNumber),

            QuestionType.MultipleChoiceMulti
                => new ChoiceMultiQuestionViewModel(source, displayNumber),

            QuestionType.MatchingHeadings
                or QuestionType.MatchingInformation
                or QuestionType.MatchingFeatures
                or QuestionType.MatchingSentenceEndings
                => new MatchingQuestionViewModel(source, displayNumber, sharedOptions),

            // Map/Diagram labeling has two flavours: pick-from-list (use the matching dropdown VM)
            // and write-words-from-passage (text input with NMTW limit). The data shape decides.
            QuestionType.MapLabeling or QuestionType.DiagramLabeling
                => HasLabelOptions(source, sharedOptions)
                    ? new MatchingQuestionViewModel(source, displayNumber, sharedOptions)
                    : new TextInputQuestionViewModel(source, displayNumber),

            QuestionType.SentenceCompletion
                or QuestionType.SummaryCompletion
                or QuestionType.NoteCompletion
                or QuestionType.TableCompletion
                or QuestionType.FlowChartCompletion
                or QuestionType.ShortAnswer
                or QuestionType.FormCompletion
                => new TextInputQuestionViewModel(source, displayNumber),

            _ => throw new NotSupportedException($"Question type {source.Type} is not yet supported.")
        };
    }

    private static bool HasLabelOptions(TestQuestion source, IReadOnlyList<string>? sharedOptions)
        => (sharedOptions is not null && sharedOptions.Count > 0)
           || !string.IsNullOrWhiteSpace(source.OptionsJson);
}
