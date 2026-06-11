using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Writing;

public partial class WritingTaskViewModel : ObservableObject
{
    private readonly IWritingTaskService _taskSvc;
    private readonly WritingFeedbackService _feedback;
    private readonly ILogger<WritingTaskViewModel> _log;

    private DispatcherTimer? _timer;
    private DateTime _startedAt;
    private int _attemptId;

    [ObservableProperty] private WritingTask? _task;
    [ObservableProperty] private string _userText = string.Empty;
    [ObservableProperty] private int _wordCount;
    [ObservableProperty] private int _elapsedSeconds;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public string Title => Task?.Code ?? string.Empty;
    public string PromptText => Task?.PromptText ?? string.Empty;
    public int MinWords => Task?.MinWords ?? 0;
    public int RecommendedMinutes => Task?.RecommendedMinutes ?? 0;
    public string KindLabel => Task?.Kind switch
    {
        WritingTaskKind.Task1Academic => "Task 1 Academic",
        WritingTaskKind.Task1GeneralTraining => "Task 1 General Training",
        WritingTaskKind.Task2 => "Task 2",
        _ => string.Empty
    };

    public bool MeetsWordCount => WordCount >= MinWords;

    public string ElapsedDisplay
    {
        get
        {
            var m = ElapsedSeconds / 60;
            var s = ElapsedSeconds % 60;
            return $"{m:D2}:{s:D2}";
        }
    }

    public event Action<int>? Submitted;
    public event Action? Cancelled;

    public WritingTaskViewModel(
        IWritingTaskService taskSvc,
        WritingFeedbackService feedback,
        ILogger<WritingTaskViewModel> log)
    {
        _taskSvc = taskSvc;
        _feedback = feedback;
        _log = log;
    }

    public async Task StartAsync(int taskId)
    {
        Task = await _taskSvc.GetFullAsync(taskId);
        if (Task is null)
        {
            StatusText = Loc.Tr("Writing_TaskNotFound");
            return;
        }

        var attempt = await _taskSvc.StartAttemptAsync(taskId);
        _attemptId = attempt.Id;
        _startedAt = attempt.StartedAt;
        UserText = string.Empty;
        WordCount = 0;
        ElapsedSeconds = 0;
        StatusText = string.Empty;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(PromptText));
        OnPropertyChanged(nameof(MinWords));
        OnPropertyChanged(nameof(RecommendedMinutes));
        OnPropertyChanged(nameof(KindLabel));

        StartTimer();
    }

    partial void OnUserTextChanged(string value)
    {
        WordCount = IeltsWordCounter.Count(value);
        OnPropertyChanged(nameof(MeetsWordCount));
        SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnElapsedSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(ElapsedDisplay));
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

    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (_attemptId == 0) return;
        IsBusy = true;
        StatusText = Loc.Tr("Writing_Saving");
        try
        {
            await _taskSvc.SaveDraftAsync(_attemptId, UserText);
            StatusText = Loc.Tr("Writing_DraftSaved");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save writing draft");
            StatusText = Loc.Tr("Writing_SaveDraftFailed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (_attemptId == 0) return;
        IsBusy = true;
        StatusText = Loc.Tr("Writing_SubmittingAndEvaluating");
        try
        {
            await _taskSvc.SubmitAttemptAsync(_attemptId, UserText);
            _timer?.Stop();

            try
            {
                var report = await _feedback.EvaluateAndSaveAsync(_attemptId);
                if (report is null)
                {
                    StatusText = Loc.Tr("Writing_AnswerSavedNoAi");
                }
            }
            catch (Exception evalEx)
            {
                _log.LogError(evalEx, "Essay evaluator failed");
                StatusText = Loc.Tr("Writing_AnswerSavedAiFailed") + evalEx.Message;
            }

            Submitted?.Invoke(_attemptId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to submit writing attempt");
            StatusText = Loc.Tr("Writing_SubmitAnswerFailed");
        }
        finally { IsBusy = false; }
    }

    private bool CanSubmit() => !IsBusy && WordCount > 0;

    [RelayCommand]
    private void Cancel()
    {
        _timer?.Stop();
        Cancelled?.Invoke();
    }
}
