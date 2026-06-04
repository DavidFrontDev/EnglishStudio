using System.Windows;
using System.Windows.Controls;
using EnglishStudio.App.ViewModels.Speaking;

namespace EnglishStudio.App.Views.Speaking;

public partial class SpeakingPart3View : UserControl
{
    public SpeakingPart3View()
    {
        InitializeComponent();
    }

    private void OnRecordRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart3ViewModel vm && vm.StartRecordCommand.CanExecute(null))
            vm.StartRecordCommand.Execute(null);
    }

    private void OnStopRequested(object sender, RoutedEventArgs e)
    {
        if (DataContext is SpeakingPart3ViewModel vm && vm.StopRecordCommand.CanExecute(null))
            vm.StopRecordCommand.Execute(null);
    }
}
