using EnglishStudio.App.Shell;

namespace EnglishStudio.App.Views.Settings;

public partial class SettingsWindow : ChromedWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as ViewModels.SettingsViewModel)?.Cleanup();
    }
}
