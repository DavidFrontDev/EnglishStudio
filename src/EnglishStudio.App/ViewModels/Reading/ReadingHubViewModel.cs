using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Content;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Reading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Reading;

public partial class ReadingHubViewModel : ObservableObject
{
    private readonly IReadingTestService _testSvc;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly IServiceProvider _services;
    private readonly ILogger<ReadingHubViewModel> _log;

    public ObservableCollection<ReadingTestSummary> Tests { get; } = new();
    public ObservableCollection<ReadingAttemptSummary> RecentAttempts { get; } = new();

    [ObservableProperty] private ReadingTestSummary? _selectedTest;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>True when the Reading content pack isn't imported — shows the gating banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText = Loc.Tr("ReadIelts_ContentMissingText");

    /// <summary>The currently displayed sub-view: this hub, an active test, or a results screen.</summary>
    [ObservableProperty] private object? _currentScreen;

    public bool IsHubVisible => CurrentScreen is null;

    public ReadingHubViewModel(
        IReadingTestService testSvc,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IServiceProvider services,
        ILogger<ReadingHubViewModel> log)
    {
        _testSvc = testSvc;
        _content = content;
        _importLauncher = importLauncher;
        _services = services;
        _log = log;
        CurrentScreen = null;

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
            IsContentMissing = !_content.IsImported(ContentSection.Reading);
            if (IsContentMissing)
            {
                Tests.Clear();
                RecentAttempts.Clear();
                return;
            }

            var tests = await _testSvc.ListAsync();
            Tests.Clear();
            foreach (var t in tests) Tests.Add(t);
            SelectedTest ??= Tests.FirstOrDefault();

            var history = await _testSvc.ListAttemptsAsync(20);
            RecentAttempts.Clear();
            foreach (var a in history) RecentAttempts.Add(a);
            ClearHistoryCommand.NotifyCanExecuteChanged();

            if (Tests.Count == 0)
            {
                StatusText = Loc.Tr("ReadIelts_NoTestsGenerated");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load reading hub data");
            StatusText = Loc.Tr("ReadIelts_LoadTestsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartExamAsync() => await StartInternalAsync(trainingMode: false);

    [RelayCommand(CanExecute = nameof(CanStartTraining))]
    private async Task StartTrainingAsync() => await StartInternalAsync(trainingMode: true);

    private bool CanStart() => SelectedTest is not null;

    /// <summary>Training-mode is unavailable for exam-only tests.</summary>
    private bool CanStartTraining() => SelectedTest is not null && !SelectedTest.IsExamOnly;

    partial void OnSelectedTestChanged(ReadingTestSummary? value)
    {
        StartExamCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
    }

    private async Task StartInternalAsync(bool trainingMode)
    {
        if (SelectedTest is null) return;

        var testVm = _services.GetRequiredService<ReadingTestViewModel>();
        testVm.Finished += async (attemptId) =>
        {
            await ShowResultAsync(attemptId);
        };
        testVm.Cancelled += async () =>
        {
            (CurrentScreen as IDisposable)?.Dispose();
            CurrentScreen = null;
            await LoadAsync();
            OnPropertyChanged(nameof(IsHubVisible));
        };

        await testVm.StartAsync(SelectedTest.Id, trainingMode);
        (CurrentScreen as IDisposable)?.Dispose();
        CurrentScreen = testVm;
        OnPropertyChanged(nameof(IsHubVisible));
    }

    [RelayCommand]
    private async Task OpenAttemptAsync(ReadingAttemptSummary? summary)
    {
        if (summary is null) return;
        await ShowResultAsync(summary.Id);
    }

    [RelayCommand(CanExecute = nameof(HasHistory))]
    private async Task ClearHistoryAsync()
    {
        var total = await _testSvc.CountAttemptsAsync();
        var confirm = ConfirmWindow.Show(
            System.Windows.Application.Current.MainWindow,
            Loc.Tr("ReadIelts_ClearHistoryTitle"),
            Loc.Format("ReadIelts_ClearHistoryBody", total),
            confirmText: Loc.Tr("ReadIelts_ClearHistoryConfirm"));

        if (!confirm) return;

        try
        {
            var removed = await _testSvc.ClearAllAttemptsAsync();
            _log.LogInformation("Reading hub: removed {N} attempt(s).", removed);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear reading attempts");
            StatusText = Loc.Tr("ReadIelts_ClearHistoryFailed");
        }
        await LoadAsync();
    }

    private bool HasHistory() => RecentAttempts.Count > 0;

    private async Task ShowResultAsync(int attemptId)
    {
        var resultVm = _services.GetRequiredService<ReadingResultViewModel>();
        await resultVm.LoadAsync(attemptId);

        // Wrap into a tiny holder so the View can present a "Назад" button.
        var holder = new ReadingResultScreen(resultVm, async () =>
        {
            CurrentScreen = null;
            await LoadAsync();
            OnPropertyChanged(nameof(IsHubVisible));
        });
        (CurrentScreen as IDisposable)?.Dispose();
        CurrentScreen = holder;
        OnPropertyChanged(nameof(IsHubVisible));
    }
}

public sealed partial class ReadingResultScreen : ObservableObject
{
    private readonly Func<Task> _onBack;

    public ReadingResultViewModel Result { get; }

    public ReadingResultScreen(ReadingResultViewModel result, Func<Task> onBack)
    {
        Result = result;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task BackAsync() => await _onBack();
}
