using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>Handles Short Answer and every *Completion variant (free-text input with NMTW limit).</summary>
public partial class TextInputQuestionViewModel : ObservableObject, IReadingQuestionViewModel
{
    [ObservableProperty]
    private string _text = string.Empty;

    public int QuestionId { get; }
    public int DisplayNumber { get; }
    public string Stem { get; }
    public QuestionType Type { get; }
    public int? WordLimitMax { get; }

    public bool HasAnswer => !string.IsNullOrWhiteSpace(Text);
    public int WordCount => AnswerNormalization.CountWords(Text);
    public bool ExceedsLimit => WordLimitMax.HasValue && WordCount > WordLimitMax.Value;

    public string WordLimitLabel => WordLimitMax.HasValue
        ? Loc.Format("ReadIelts_WordLimit", WordLimitMax.Value)
        : string.Empty;

    public TextInputQuestionViewModel(TestQuestion source, int displayNumber)
    {
        QuestionId = source.Id;
        DisplayNumber = displayNumber;
        Stem = source.Stem;
        Type = source.Type;
        WordLimitMax = source.WordLimitMax;
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasAnswer));
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(ExceedsLimit));
    }

    public string GetAnswerJson() => JsonSerializer.Serialize(Text);

}
