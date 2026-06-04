using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EnglishStudio.App.ViewModels.Writing;

/// <summary>
/// Drives the small "AI is evaluating..." dialog: animated spinner state owned by the View,
/// but elapsed-since-submit and status text live here so the orchestrator can update them.
/// </summary>
public partial class AiProcessingViewModel : ObservableObject
{
    private DispatcherTimer? _timer;
    private DateTime _startedAt;

    [ObservableProperty] private string _statusText = "Отправка задания в AI…";
    [ObservableProperty] private int _elapsedSeconds;

    public string ElapsedDisplay
    {
        get
        {
            var m = ElapsedSeconds / 60;
            var s = ElapsedSeconds % 60;
            return $"{m:D2}:{s:D2}";
        }
    }

    partial void OnElapsedSecondsChanged(int value) => OnPropertyChanged(nameof(ElapsedDisplay));

    public void Start()
    {
        _startedAt = DateTime.UtcNow;
        ElapsedSeconds = 0;
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ElapsedSeconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}
