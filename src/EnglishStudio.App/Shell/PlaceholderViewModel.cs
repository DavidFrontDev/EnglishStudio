using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Localization;

namespace EnglishStudio.App.Shell;

public partial class PlaceholderViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = Loc.Tr("Shell_PlaceholderTitle");

    [ObservableProperty]
    private string _description = Loc.Tr("Shell_PlaceholderDescription");

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
