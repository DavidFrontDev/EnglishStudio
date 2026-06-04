using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>Handles True/False/Not Given and Yes/No/Not Given questions.</summary>
public partial class TfngQuestionViewModel : ObservableObject, IReadingQuestionViewModel
{
    [ObservableProperty]
    private string? _selectedAnswer;

    public int QuestionId { get; }
    public int DisplayNumber { get; }
    public string Stem { get; }
    public QuestionType Type { get; }
    public IReadOnlyList<string> Options { get; }

    public bool HasAnswer => !string.IsNullOrEmpty(SelectedAnswer);

    public TfngQuestionViewModel(TestQuestion source, int displayNumber)
    {
        QuestionId = source.Id;
        DisplayNumber = displayNumber;
        Stem = source.Stem;
        Type = source.Type;
        Options = source.Type == QuestionType.YesNoNotGiven
            ? new[] { "YES", "NO", "NOT GIVEN" }
            : new[] { "TRUE", "FALSE", "NOT GIVEN" };
    }

    partial void OnSelectedAnswerChanged(string? value) => OnPropertyChanged(nameof(HasAnswer));

    public string GetAnswerJson() => JsonSerializer.Serialize(SelectedAnswer ?? string.Empty);
}
