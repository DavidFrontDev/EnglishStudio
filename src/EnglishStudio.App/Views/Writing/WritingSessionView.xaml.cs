using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnglishStudio.App.ViewModels.Writing;

namespace EnglishStudio.App.Views.Writing;

public partial class WritingSessionView : UserControl
{
    private WritingSessionViewModel? _vm;

    public WritingSessionView()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => FocusAnswerTextBox();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as WritingSessionViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WritingSessionViewModel.CurrentTask))
        {
            // Queue focus after the binding has flowed through.
            Dispatcher.BeginInvoke(new Action(FocusAnswerTextBox), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void FocusAnswerTextBox()
    {
        if (AnswerTextBox is null) return;
        AnswerTextBox.CaretIndex = 0;
        AnswerTextBox.Focus();
        Keyboard.Focus(AnswerTextBox);
        AnswerTextBox.ScrollToHome();
    }
}
