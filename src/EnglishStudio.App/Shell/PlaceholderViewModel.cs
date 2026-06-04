using CommunityToolkit.Mvvm.ComponentModel;

namespace EnglishStudio.App.Shell;

public partial class PlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Скоро доступно";

    [ObservableProperty]
    private string _description = "Этот раздел появится в следующих обновлениях.";

    [ObservableProperty]
    private string _icon = "🚧";

    public PlaceholderViewModel() { }

    public PlaceholderViewModel(string icon, string title, string description)
    {
        Icon = icon;
        Title = title;
        Description = description;
    }
}
