using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Content;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Listening;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Listening;

public partial class ListeningHubViewModel : ObservableObject
{
    private readonly IListeningTestService _testSvc;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly IServiceProvider _services;
    private readonly ILogger<ListeningHubViewModel> _log;

    public ObservableCollection<ListeningTestSummary> Tests { get; } = new();
    public ObservableCollection<ListeningAttemptSummary> RecentAttempts { get; } = new();

    [ObservableProperty] private ListeningTestSummary? _selectedTest;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private object? _currentScreen;

    /// <summary>True when the Listening content pack isn't imported — shows the gating banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText =
        Loc.Tr("Listening_ContentMissing");

    public bool IsHubVisible => CurrentScreen is null;

    public ListeningHubViewModel(
        IListeningTestService testSvc,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IServiceProvider services,
        ILogger<ListeningHubViewModel> log)
    {
        _testSvc = testSvc;
        _content = content;
        _importLauncher = importLauncher;
        _services = services;
        _log = log;
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
            IsContentMissing = !_content.IsImported(ContentSection.Listening);
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
                StatusText = Loc.Tr("Listening_NoTestsInSeed");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load listening hub data");
            StatusText = Loc.Tr("Listening_LoadTestsFailed");
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
    private bool CanStartTraining() => SelectedTest is not null && !SelectedTest.IsExamOnly;

    partial void OnSelectedTestChanged(ListeningTestSummary? value)
    {
        StartExamCommand.NotifyCanExecuteChanged();
        StartTrainingCommand.NotifyCanExecuteChanged();
    }

    private async Task StartInternalAsync(bool trainingMode)
    {
        if (SelectedTest is null) return;

        var sessionVm = _services.GetRequiredService<ListeningSessionViewModel>();
        sessionVm.Finished += async (attemptId) => await ShowResultAsync(attemptId);
        sessionVm.Cancelled += async () =>
        {
            (CurrentScreen as IDisposable)?.Dispose();
            CurrentScreen = null;
            await LoadAsync();
            OnPropertyChanged(nameof(IsHubVisible));
        };

        await sessionVm.StartAsync(SelectedTest.Id, trainingMode);
        (CurrentScreen as IDisposable)?.Dispose();
        CurrentScreen = sessionVm;
        OnPropertyChanged(nameof(IsHubVisible));
    }

    [RelayCommand]
    private async Task OpenAttemptAsync(ListeningAttemptSummary? summary)
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
            Loc.Tr("Listening_ClearHistoryTitle"),
            Loc.Format("Listening_ClearHistoryConfirm", total),
            confirmText: Loc.Tr("Listening_ClearHistoryConfirmBtn"));
        if (!confirm) return;

        try
        {
            var removed = await _testSvc.ClearAllAttemptsAsync();
            _log.LogInformation("Listening hub: removed {N} attempt(s).", removed);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear listening attempts");
            StatusText = Loc.Tr("Listening_ClearHistoryFailed");
        }
        await LoadAsync();
    }

    private bool HasHistory() => RecentAttempts.Count > 0;

    private async Task ShowResultAsync(int attemptId)
    {
        var resultVm = _services.GetRequiredService<ListeningResultViewModel>();
        await resultVm.LoadAsync(attemptId);
        var holder = new ListeningResultScreen(resultVm, async () =>
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

public sealed partial class ListeningResultScreen : ObservableObject
{
    private readonly Func<Task> _onBack;
    public ListeningResultViewModel Result { get; }

    public ListeningResultScreen(ListeningResultViewModel result, Func<Task> onBack)
    {
        Result = result;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task BackAsync() => await _onBack();
}
