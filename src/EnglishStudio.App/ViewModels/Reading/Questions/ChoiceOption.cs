using CommunityToolkit.Mvvm.ComponentModel;

namespace EnglishStudio.App.ViewModels.Reading.Questions;

public partial class ChoiceOption : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string Tag { get; }
    public string Text { get; }

    public ChoiceOption(string tag, string text)
    {
        Tag = tag;
        Text = text;
    }
}
