using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Writing;

/// <summary>
/// Drives a full Writing test session: one timer for both Task 1 and Task 2,
/// tab navigation between the two, and a single "Finish test" action that submits both.
/// </summary>
public partial class WritingSessionViewModel : ObservableObject
{
    private const int AutoSaveDebounceMs = 400;

    private readonly IWritingTaskService _taskSvc;
    private readonly ILogger<WritingSessionViewModel> _log;

    private DispatcherTimer? _timer;
    private DateTime _startedAt;
    private readonly Dictionary<int, DispatcherTimer> _saveDebounce = new();
    private bool _saveInFlight;

    [ObservableProperty] private TestSet? _testSet;
    [ObservableProperty] private WritingSessionTaskViewModel? _task1;
    [ObservableProperty] private WritingSessionTaskViewModel? _task2;
    [ObservableProperty] private WritingSessionTaskViewModel? _currentTask;
    [ObservableProperty] private int _elapsedSeconds;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Real IELTS Writing total = 60 min. We display elapsed up to this target.</summary>
    public int TotalRecommendedSeconds => 60 * 60;

    public string ElapsedDisplay
    {
        get
        {
            var m = ElapsedSeconds / 60;
            var s = ElapsedSeconds % 60;
            return $"{m:D2}:{s:D2}";
        }
    }

    public string TestTitle => TestSet?.Title ?? string.Empty;
    public bool IsTask1Current => CurrentTask == Task1;
    public bool IsTask2Current => CurrentTask == Task2;

    /// <summary>Fired after both attempts are saved as submitted but before AI evaluation.</summary>
    public event Action<IReadOnlyList<int>>? Submitted;
    /// <summary>Fired when the user pressed Cancel — caller is expected to discard the attempts.</summary>
    public event Action<IReadOnlyList<int>>? Cancelled;

    public WritingSessionViewModel(
        IWritingTaskService taskSvc,
        ILogger<WritingSessionViewModel> log)
    {
        _taskSvc = taskSvc;
        _log = log;
    }

    public async Task StartAsync(int testSetId)
    {
        var detail = await _taskSvc.GetTestSetAsync(testSetId);
        if (detail is null)
        {
            StatusText = Loc.Tr("Writing_TestNotFound");
            return;
        }

        TestSet = detail.TestSet;

        var attempt1 = await _taskSvc.StartAttemptAsync(detail.Task1.Id);
        var attempt2 = await _taskSvc.StartAttemptAsync(detail.Task2.Id);

        Task1 = new WritingSessionTaskViewModel(detail.Task1, attempt1.Id);
        Task2 = new WritingSessionTaskViewModel(detail.Task2, attempt2.Id);
        CurrentTask = Task1;

        Task1.UserTextChanged += AutoSaveDraft;
        Task2.UserTextChanged += AutoSaveDraft;

        _startedAt = DateTime.UtcNow;
        ElapsedSeconds = 0;
        StartTimer();

        OnPropertyChanged(nameof(TestTitle));
        OnPropertyChanged(nameof(IsTask1Current));
        OnPropertyChanged(nameof(IsTask2Current));
    }

    partial void OnCurrentTaskChanged(WritingSessionTaskViewModel? value)
    {
        OnPropertyChanged(nameof(IsTask1Current));
        OnPropertyChanged(nameof(IsTask2Current));
    }

    partial void OnElapsedSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(ElapsedDisplay));
    }

    [RelayCommand]
    private void SwitchToTask1()
    {
        if (Task1 != null) CurrentTask = Task1;
    }

    [RelayCommand]
    private void SwitchToTask2()
    {
        if (Task2 != null) CurrentTask = Task2;
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            ElapsedSeconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
        };
        _timer.Start();
    }

    private void AutoSaveDraft(WritingSessionTaskViewModel sender)
    {
        if (_isCleanedUp) return;
        if (!_saveDebounce.TryGetValue(sender.AttemptId, out var t))
        {
            t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoSaveDebounceMs) };
            t.Tick += async (_, _) =>
            {
                t.Stop();
                if (_isCleanedUp) return;
                if (_saveInFlight) { t.Start(); return; }
                _saveInFlight = true;
                try { await _taskSvc.SaveDraftAsync(sender.AttemptId, sender.UserText); }
                catch (Exception ex) { _log.LogWarning(ex, "Auto-save draft failed"); }
                finally { _saveInFlight = false; }
            };
            _saveDebounce[sender.AttemptId] = t;
        }
        t.Stop();
        t.Start();
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (Task1 is null || Task2 is null) return;

        _timer?.Stop();
        foreach (var t in _saveDebounce.Values) t.Stop();
        IsBusy = true;
        StatusText = Loc.Tr("Writing_SubmittingAnswers");

        try
        {
            await _taskSvc.SubmitAttemptAsync(Task1.AttemptId, Task1.UserText);
            await _taskSvc.SubmitAttemptAsync(Task2.AttemptId, Task2.UserText);

            Submitted?.Invoke(new[] { Task1.AttemptId, Task2.AttemptId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to submit writing session");
            StatusText = Loc.Tr("Writing_SubmitAnswersFailed") + ex.Message;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Cleanup();
        var ids = new List<int>(2);
        if (Task1 != null) ids.Add(Task1.AttemptId);
        if (Task2 != null) ids.Add(Task2.AttemptId);
        Cancelled?.Invoke(ids);
    }

    private bool _isCleanedUp;

    /// <summary>Stops the session timer. Safe to call more than once (Cancel + window Closed).</summary>
    public void Cleanup()
    {
        if (_isCleanedUp) return;
        _isCleanedUp = true;
        _timer?.Stop();
        foreach (var t in _saveDebounce.Values) t.Stop();
        _saveDebounce.Clear();
    }
}

/// <summary>
/// Lightweight pane VM for a single task inside a session. No timer (session owns it),
/// no submit (session owns it) — just prompt, image, text input and word count.
/// </summary>
public partial class WritingSessionTaskViewModel : ObservableObject
{
    [ObservableProperty] private string _userText = string.Empty;
    [ObservableProperty] private int _wordCount;

    public WritingTask Task { get; }
    public int AttemptId { get; }

    public string PromptText => Task.PromptText;
    public string? ImagePath => Task.ImagePath;
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
    public int MinWords => Task.MinWords;
    public int RecommendedMinutes => Task.RecommendedMinutes;
    public string KindLabel => Task.Kind switch
    {
        WritingTaskKind.Task1Academic => "Task 1",
        WritingTaskKind.Task1GeneralTraining => "Task 1 GT",
        WritingTaskKind.Task2 => "Task 2",
        _ => string.Empty
    };
    public bool MeetsWordCount => WordCount >= MinWords;

    public event Action<WritingSessionTaskViewModel>? UserTextChanged;

    public WritingSessionTaskViewModel(WritingTask task, int attemptId)
    {
        Task = task;
        AttemptId = attemptId;
    }

    partial void OnUserTextChanged(string value)
    {
        WordCount = IeltsWordCounter.Count(value);
        OnPropertyChanged(nameof(MeetsWordCount));
        UserTextChanged?.Invoke(this);
    }
}
