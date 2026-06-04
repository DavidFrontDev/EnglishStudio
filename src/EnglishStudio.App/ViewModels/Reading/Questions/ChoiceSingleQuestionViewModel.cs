using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>MultipleChoiceSingle — radio-button style, one of A/B/C/D.</summary>
public partial class ChoiceSingleQuestionViewModel : ObservableObject, IReadingQuestionViewModel
{
    public int QuestionId { get; }
    public int DisplayNumber { get; }
    public string Stem { get; }
    public QuestionType Type { get; }
    public ObservableCollection<ChoiceOption> Options { get; }
    public string GroupName { get; }

    public bool HasAnswer => Options.Any(o => o.IsSelected);

    public ChoiceSingleQuestionViewModel(TestQuestion source, int displayNumber)
    {
        QuestionId = source.Id;
        DisplayNumber = displayNumber;
        Stem = source.Stem;
        Type = source.Type;
        GroupName = $"q{source.Id}";

        var rawOptions = QuestionOptionParser.ParseStringArray(source.OptionsJson);
        Options = new ObservableCollection<ChoiceOption>(
            rawOptions.Select((text, i) => new ChoiceOption(((char)('A' + i)).ToString(), StripPrefix(text))));

        foreach (var opt in Options)
        {
            opt.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ChoiceOption.IsSelected))
                {
                    OnPropertyChanged(nameof(HasAnswer));
                }
            };
        }
    }

    public string GetAnswerJson()
    {
        var selected = Options.FirstOrDefault(o => o.IsSelected);
        return JsonSerializer.Serialize(selected?.Tag ?? string.Empty);
    }

    private static string StripPrefix(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length >= 3 && char.IsLetter(trimmed[0]))
        {
            for (var i = 1; i < Math.Min(trimmed.Length, 4); i++)
            {
                if (trimmed[i] == '—' || trimmed[i] == '-' || trimmed[i] == ':')
                {
                    return trimmed[(i + 1)..].Trim();
                }
            }
        }
        return text;
    }
}
