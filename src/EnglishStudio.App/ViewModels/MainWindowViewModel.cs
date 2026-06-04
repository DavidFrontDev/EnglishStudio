using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Shell;

namespace EnglishStudio.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "EnglishStudio — IELTS-тренажёр";

    public ShellViewModel Shell { get; }

    public MainWindowViewModel(ShellViewModel shell)
    {
        Shell = shell;
        Shell.PropertyChanged += OnShellPropertyChanged;
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentModule) &&
            Shell.CurrentModule?.Code == "stats")
        {
            // Refresh stats VM when its module is selected — preserves M5 behaviour.
            if (Shell.CurrentView is System.Windows.FrameworkElement fe &&
                fe.DataContext is StatsViewModel statsVm)
            {
                _ = statsVm.RefreshAsync();
            }
        }
    }
}
