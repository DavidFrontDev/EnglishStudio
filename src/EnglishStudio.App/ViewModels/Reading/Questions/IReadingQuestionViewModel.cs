using System.ComponentModel;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>
/// Common surface for all reading-question view models. The host (ReadingTestViewModel)
/// listens to <see cref="INotifyPropertyChanged"/> and persists answers via the runner.
/// </summary>
public interface IReadingQuestionViewModel : INotifyPropertyChanged
{
    int QuestionId { get; }
    int DisplayNumber { get; }
    string Stem { get; }
    QuestionType Type { get; }
    bool HasAnswer { get; }
    string GetAnswerJson();
}
