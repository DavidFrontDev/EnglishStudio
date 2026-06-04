using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EnglishStudio.App.ViewModels.Speaking;

namespace EnglishStudio.App.Views.Speaking;

public partial class SpeakingResultView : UserControl
{
    public SpeakingResultView()
    {
        InitializeComponent();
    }

    // Пока пользователь тащит ползунок — таймер не должен перебивать позицию (см. BeginScrub/EndScrub).
    private static SpeakingResponseRow? RowOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as SpeakingResponseRow;

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => RowOf(sender)?.BeginScrub();

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e) => RowOf(sender)?.EndScrub();
}
