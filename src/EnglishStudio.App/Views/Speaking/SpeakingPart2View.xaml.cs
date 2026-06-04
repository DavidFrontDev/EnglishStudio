using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Speaking;

namespace EnglishStudio.App.Views.Speaking;

public partial class SpeakingPart2View : UserControl
{
    public SpeakingPart2View()
    {
        InitializeComponent();
    }

    private void OnRecordRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart2ViewModel vm && vm.StartRecordCommand.CanExecute(null))
            vm.StartRecordCommand.Execute(null);
    }

    private void OnStopRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart2ViewModel vm && vm.StopRecordCommand.CanExecute(null))
            vm.StopRecordCommand.Execute(null);
    }
}
