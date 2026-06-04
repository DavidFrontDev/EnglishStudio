using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.ViewModels.Reading;

/// <summary>
/// One cell of the "Question Map" navigation grid: shows the display number,
/// reflects answered/unanswered status, and remembers which part holds the question
/// so the host can jump to it.
/// </summary>
public sealed partial class QuestionMapCellViewModel : ObservableObject
{
    public int DisplayNumber { get; }
    public int PartIndex { get; }
    public IReadingQuestionViewModel Question { get; }

    [ObservableProperty] private bool _isAnswered;

    public QuestionMapCellViewModel(int displayNumber, int partIndex, IReadingQuestionViewModel question)
    {
        DisplayNumber = displayNumber;
        PartIndex = partIndex;
        Question = question;
        IsAnswered = question.HasAnswer;
    }

    public void Refresh() => IsAnswered = Question.HasAnswer;
}
