using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

/// <summary>MultipleChoiceMulti — checkbox style, select N correct options.</summary>
public partial class ChoiceMultiQuestionViewModel : ObservableObject, IReadingQuestionViewModel
{
    public int QuestionId { get; }
    public int DisplayNumber { get; }
    public string Stem { get; }
    public QuestionType Type { get; }
    public ObservableCollection<ChoiceOption> Options { get; }

    public bool HasAnswer => Options.Any(o => o.IsSelected);

    public ChoiceMultiQuestionViewModel(TestQuestion source, int displayNumber)
    {
        QuestionId = source.Id;
        DisplayNumber = displayNumber;
        Stem = source.Stem;
        Type = source.Type;

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
        var selected = Options.Where(o => o.IsSelected).Select(o => o.Tag).ToArray();
        return JsonSerializer.Serialize(selected);
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
