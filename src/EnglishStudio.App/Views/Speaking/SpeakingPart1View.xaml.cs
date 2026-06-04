using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Speaking;

namespace EnglishStudio.App.Views.Speaking;

public partial class SpeakingPart1View : UserControl
{
    public SpeakingPart1View()
    {
        InitializeComponent();
    }

    private void OnRecordRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart1ViewModel vm && vm.StartRecordCommand.CanExecute(null))
            vm.StartRecordCommand.Execute(null);
    }

    private void OnStopRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart1ViewModel vm && vm.StopRecordCommand.CanExecute(null))
            vm.StopRecordCommand.Execute(null);
    }
}
