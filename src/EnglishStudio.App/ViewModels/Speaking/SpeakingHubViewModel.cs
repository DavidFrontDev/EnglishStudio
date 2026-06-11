using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Content;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.App.Views.Speaking;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Speaking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Speaking;

public partial class SpeakingHubViewModel : ObservableObject
{
    private readonly ISpeakingTestService _svc;
    private readonly ISpeakingFeedbackService _feedback;
    private readonly IWhisperTranscriber _whisper;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly IClaudeCliClient _claudeCli;
    private readonly IServiceProvider _services;
    private readonly ILogger<SpeakingHubViewModel> _log;

    private SpeakingSessionWindow? _sessionWindow;

    public ObservableCollection<SpeakingTopicSummary> Part1Topics { get; } = new();
    public ObservableCollection<SpeakingTopicSummary> Part2Topics { get; } = new();
    public ObservableCollection<SpeakingAttemptSummary> RecentAttempts { get; } = new();

    public ICollectionView Part1TopicsView { get; }
    public ICollectionView Part2TopicsView { get; }

    [ObservableProperty] private SpeakingMode _selectedMode = SpeakingMode.FullMock;
    [ObservableProperty] private SpeakingTopicSummary? _selectedPart1Topic;
    [ObservableProperty] private SpeakingTopicSummary? _selectedPart2Topic;
    [ObservableProperty] private string _topicFilter = string.Empty;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private object? _currentScreen;

    /// <summary>True when the Speaking content pack isn't imported — shows the gating banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText = Loc.Tr("Speaking_ContentMissingText");

    /// <summary>True when the Claude CLI isn't available — shows the AI-unavailable banner.</summary>
    [ObservableProperty] private bool _isAiUnavailable;

    public bool IsHubVisible => CurrentScreen is null;

    public bool ShowsPart1Picker => SelectedMode == SpeakingMode.Part1Only;
    public bool ShowsPart2Picker =>
        SelectedMode == SpeakingMode.Part2Only ||
        SelectedMode == SpeakingMode.Part3Only;
    public bool ShowsRandomNote => SelectedMode == SpeakingMode.FullMock;

    public string ModeDescription => SelectedMode switch
    {
        SpeakingMode.FullMock   => Loc.Tr("Speaking_ModeDescFullMock"),
        SpeakingMode.Part1Only  => Loc.Tr("Speaking_ModeDescPart1Only"),
        SpeakingMode.Part2Only  => Loc.Tr("Speaking_ModeDescPart2Only"),
        SpeakingMode.Part3Only  => Loc.Tr("Speaking_ModeDescPart3Only"),
        _ => string.Empty
    };

    public SpeakingHubViewModel(
        ISpeakingTestService svc,
        ISpeakingFeedbackService feedback,
        IWhisperTranscriber whisper,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IClaudeCliClient claudeCli,
        IServiceProvider services,
        ILogger<SpeakingHubViewModel> log)
    {
        _svc = svc;
        _feedback = feedback;
        _whisper = whisper;
        _content = content;
        _importLauncher = importLauncher;
        _claudeCli = claudeCli;
        _services = services;
        _log = log;

        Part1TopicsView = CollectionViewSource.GetDefaultView(Part1Topics);
        Part1TopicsView.Filter = MatchesTopicFilter;
        Part2TopicsView = CollectionViewSource.GetDefaultView(Part2Topics);
        Part2TopicsView.Filter = MatchesTopicFilter;

        _ = LoadAsync();
    }

    /// <summary>Opens the content importer, then reloads (picks up freshly imported content).</summary>
    [RelayCommand]
    private async Task OpenImportAsync()
    {
        _importLauncher.Show();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = string.Empty;
        try
        {
            IsAiUnavailable = !_claudeCli.IsAvailable;
            IsContentMissing = !_content.IsImported(ContentSection.Speaking);
            if (IsContentMissing)
            {
                Part1Topics.Clear();
                Part2Topics.Clear();
                RecentAttempts.Clear();
                Part1TopicsView.Refresh();
                Part2TopicsView.Refresh();
                return;
            }

            var part1 = await _svc.ListTopicsAsync(SpeakingPart.Part1);
            Part1Topics.Clear();
            foreach (var t in part1) Part1Topics.Add(t);

            var part2 = await _svc.ListTopicsAsync(SpeakingPart.Part2);
            Part2Topics.Clear();
            foreach (var t in part2) Part2Topics.Add(t);

            var history = await _svc.ListAttemptsAsync(20);
            RecentAttempts.Clear();
            foreach (var a in history) RecentAttempts.Add(a);

            Part1TopicsView.Refresh();
            Part2TopicsView.Refresh();

            if (Part1Topics.Count == 0 && Part2Topics.Count == 0)
            {
                StatusText = Loc.Tr("Speaking_ContentNotLoadedYet");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load Speaking hub data");
            StatusText = Loc.Tr("Speaking_FailedToLoadTopics");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool MatchesTopicFilter(object obj)
    {
        if (string.IsNullOrWhiteSpace(TopicFilter)) return true;
        if (obj is not SpeakingTopicSummary s) return false;
        return s.TopicLabel.Contains(TopicFilter, StringComparison.OrdinalIgnoreCase)
            || (s.CueCardPrompt?.Contains(TopicFilter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    partial void OnTopicFilterChanged(string value)
    {
        Part1TopicsView.Refresh();
        Part2TopicsView.Refresh();
    }

    partial void OnSelectedModeChanged(SpeakingMode value)
    {
        OnPropertyChanged(nameof(ShowsPart1Picker));
        OnPropertyChanged(nameof(ShowsPart2Picker));
        OnPropertyChanged(nameof(ShowsRandomNote));
        OnPropertyChanged(nameof(ModeDescription));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPart1TopicChanged(SpeakingTopicSummary? value) =>
        StartCommand.NotifyCanExecuteChanged();
    partial void OnSelectedPart2TopicChanged(SpeakingTopicSummary? value) =>
        StartCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        try
        {
            IsLoading = true;
            var progress = new Progress<string>(s =>
            {
                if (!string.IsNullOrEmpty(s)) StatusText = s;
            });
            StatusText = Loc.Tr("Speaking_CheckingWhisperModel");
            var ready = await _whisper.EnsureModelDownloadedAsync(WhisperModelSize.Medium, progress);
            if (!ready)
            {
                StatusText = Loc.Tr("Speaking_WhisperModelFailed");
                return;
            }
            StatusText = string.Empty;

            var sessionVm = _services.GetRequiredService<SpeakingSessionViewModel>();
            sessionVm.Submitted += OnSessionSubmitted;
            sessionVm.Cancelled += OnSessionCancelled;

            await sessionVm.StartAsync(SelectedMode,
                part1BankId: SelectedPart1Topic?.BankId,
                part2BankId: SelectedPart2Topic?.BankId);

            var window = new SpeakingSessionWindow
            {
                DataContext = sessionVm,
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) =>
            {
                sessionVm.Cleanup();
                sessionVm.Submitted -= OnSessionSubmitted;
                sessionVm.Cancelled -= OnSessionCancelled;
                if (ReferenceEquals(_sessionWindow, window)) _sessionWindow = null;
            };
            _sessionWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start speaking session");
            StatusText = Loc.Tr("Speaking_FailedToStartSession") + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanStart() => SelectedMode switch
    {
        SpeakingMode.FullMock => true,
        SpeakingMode.Part1Only => SelectedPart1Topic is not null,
        SpeakingMode.Part2Only => SelectedPart2Topic is not null,
        SpeakingMode.Part3Only => SelectedPart2Topic is not null,
        _ => false
    };

    private async void OnSessionSubmitted(int attemptId)
    {
        _sessionWindow?.Close();
        await ShowResultAsync(attemptId);
    }

    private async void OnSessionCancelled(int? attemptId)
    {
        _sessionWindow?.Close();
        if (attemptId is int id)
        {
            try { await _svc.DeleteAttemptAsync(id); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to discard cancelled speaking attempt"); }
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenAttemptAsync(SpeakingAttemptSummary? attempt)
    {
        if (attempt is null) return;
        await ShowResultAsync(attempt.AttemptId);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirm = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("Speaking_ClearHistoryTitle"),
            Loc.Tr("Speaking_ClearHistoryConfirm"),
            confirmText: Loc.Tr("Speaking_ClearHistoryButton"));
        if (!confirm) return;
        try
        {
            var removed = await _svc.ClearHistoryAsync();
            StatusText = removed == 0
                ? Loc.Tr("Speaking_HistoryAlreadyEmpty")
                : Loc.Format("Speaking_HistoryCleared", removed);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear speaking history");
            StatusText = Loc.Tr("Speaking_FailedToClearHistory") + ex.Message;
        }
        await LoadAsync();
    }

    private async Task ShowResultAsync(int attemptId)
    {
        var vm = _services.GetRequiredService<SpeakingResultViewModel>();
        await vm.LoadAsync(attemptId);

        var holder = new SpeakingResultScreen(vm, async () =>
        {
            vm.StopAllPlayback();
            CurrentScreen = null;
            OnPropertyChanged(nameof(IsHubVisible));
            await LoadAsync();
        });
        CurrentScreen = holder;
        OnPropertyChanged(nameof(IsHubVisible));
    }
}

public sealed partial class SpeakingResultScreen : ObservableObject
{
    private readonly Func<Task> _onBack;
    public SpeakingResultViewModel Result { get; }

    public SpeakingResultScreen(SpeakingResultViewModel result, Func<Task> onBack)
    {
        Result = result;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task BackAsync() => await _onBack();
}
