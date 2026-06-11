using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Content;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.App.Views.Writing;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Writing;

public partial class WritingHubViewModel : ObservableObject
{
    private readonly IWritingTaskService _taskSvc;
    private readonly WritingFeedbackService _feedback;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly IClaudeCliClient _claudeCli;
    private readonly IServiceProvider _services;
    private readonly ILogger<WritingHubViewModel> _log;

    private WritingSessionWindow? _sessionWindow;
    private AiProcessingWindow? _processingWindow;
    private AiProcessingViewModel? _processingVm;

    // Stable, language-neutral filter tokens. The displayed label is localized separately
    // (see FilterOption / RebuildCompletionFilters) so switching language never breaks filtering.
    public const string AllBooksToken = "";
    public const string CompletionAll = "all";
    public const string CompletionNotDone = "notdone";
    public const string CompletionDone = "done";

    public ObservableCollection<WritingTestSetSummary> TestSets { get; } = new();
    public ObservableCollection<WritingAttemptSummary> RecentAttempts { get; } = new();

    /// <summary>Live, filterable view over <see cref="TestSets"/>. Bound to the ListBox.</summary>
    public ICollectionView TestSetsView { get; }

    public ObservableCollection<FilterOption> AvailableBooks { get; } = new();
    public ObservableCollection<ChartTypeOption> AvailableChartTypes { get; } = new();
    public ObservableCollection<FilterOption> AvailableCompletionFilters { get; } = new();

    [ObservableProperty] private string _selectedBook = AllBooksToken;
    [ObservableProperty] private ChartTypeOption? _selectedChartType;
    [ObservableProperty] private string _selectedCompletion = CompletionAll;

    [ObservableProperty] private WritingTestSetSummary? _selectedTestSet;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _visibleCount;

    /// <summary>Currently displayed sub-screen: null = hub, otherwise a child VM (result screen).</summary>
    [ObservableProperty] private object? _currentScreen;

    /// <summary>True when the Writing content pack isn't imported — shows the gating banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText =
        Loc.Tr("Writing_ContentMissing");

    /// <summary>True when the Claude CLI isn't available — shows the AI-unavailable banner.</summary>
    [ObservableProperty] private bool _isAiUnavailable;

    public bool IsHubVisible => CurrentScreen is null;

    public WritingHubViewModel(
        IWritingTaskService taskSvc,
        WritingFeedbackService feedback,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IClaudeCliClient claudeCli,
        IServiceProvider services,
        ILogger<WritingHubViewModel> log)
    {
        _taskSvc = taskSvc;
        _feedback = feedback;
        _content = content;
        _importLauncher = importLauncher;
        _claudeCli = claudeCli;
        _services = services;
        _log = log;
        CurrentScreen = null;

        // Wrap the TestSets ObservableCollection in a live, filterable view so changing
        // a filter ComboBox refreshes the list without rebuilding the underlying data.
        TestSetsView = CollectionViewSource.GetDefaultView(TestSets);
        TestSetsView.Filter = MatchesFilter;

        RebuildCompletionFilters();
        // Seed the books ComboBox with the "all" option so it is non-empty before the first load.
        AvailableBooks.Add(new FilterOption(AllBooksToken, Loc.Tr("Writing_FilterAll")));

        // Re-localize filter labels live when the interface language changes.
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

        _ = LoadAsync();
    }

    /// <summary>Item displayed in the chart-type filter ComboBox.</summary>
    public sealed record ChartTypeOption(string Label, WritingChartType? Value)
    {
        public override string ToString() => Label;
    }

    /// <summary>A filter ComboBox item: a stable <see cref="Token"/> + a localized display <see cref="Label"/>.</summary>
    public sealed record FilterOption(string Token, string Label);

    private static readonly Regex BookNumberRegex = new(
        @"\bBook\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? ExtractBookNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = BookNumberRegex.Match(title);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string FormatChartType(WritingChartType ct) => ct switch
    {
        WritingChartType.LineGraph      => "Line graph",
        WritingChartType.BarChart       => "Bar chart",
        WritingChartType.PieChart       => "Pie chart",
        WritingChartType.Table          => "Table",
        WritingChartType.ProcessDiagram => "Process diagram",
        WritingChartType.Map            => "Map / plan",
        WritingChartType.MultipleCharts => "Multiple charts",
        WritingChartType.None           => "—",
        _                               => ct.ToString()
    };

    private bool MatchesFilter(object obj)
    {
        if (obj is not WritingTestSetSummary s) return false;

        // Book filter — extracted from the Title.
        if (!string.IsNullOrEmpty(SelectedBook))
        {
            var book = ExtractBookNumber(s.Title);
            if (book != SelectedBook) return false;
        }

        // Chart type filter.
        if (SelectedChartType?.Value is { } ct && s.Task1ChartType != ct) return false;

        // Completion filter.
        if (SelectedCompletion == CompletionNotDone && s.CompletedAttempts > 0) return false;
        if (SelectedCompletion == CompletionDone && s.CompletedAttempts == 0) return false;

        return true;
    }

    private void RefreshView()
    {
        TestSetsView.Refresh();
        VisibleCount = TestSetsView.Cast<object>().Count();

        // If the currently selected set was filtered out, clear it so the right pane
        // doesn't keep stale data.
        if (SelectedTestSet is { } cur && !MatchesFilter(cur))
        {
            SelectedTestSet = TestSetsView.Cast<WritingTestSetSummary>().FirstOrDefault();
        }
        else if (SelectedTestSet is null)
        {
            SelectedTestSet = TestSetsView.Cast<WritingTestSetSummary>().FirstOrDefault();
        }
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-create the filter options so their labels follow the new language (tokens are preserved).
        RebuildCompletionFilters();
        RebuildFilterOptions();
    }

    /// <summary>(Re)builds the completion filter with localized labels, preserving the selected token.</summary>
    private void RebuildCompletionFilters()
    {
        var prev = SelectedCompletion;
        AvailableCompletionFilters.Clear();
        AvailableCompletionFilters.Add(new FilterOption(CompletionAll, Loc.Tr("Writing_FilterAll")));
        AvailableCompletionFilters.Add(new FilterOption(CompletionNotDone, Loc.Tr("Writing_FilterNotDone")));
        AvailableCompletionFilters.Add(new FilterOption(CompletionDone, Loc.Tr("Writing_FilterDone")));
        SelectedCompletion = prev;
        OnPropertyChanged(nameof(SelectedCompletion));
    }

    private void RebuildFilterOptions()
    {
        // Books: derive from TestSet titles. Numeric sort, the localized "all" item stays on top.
        var books = TestSets
            .Select(t => ExtractBookNumber(t.Title))
            .Where(b => b is not null)
            .Distinct()
            .OrderBy(b => int.Parse(b!))
            .ToList();
        var prevBook = SelectedBook;
        AvailableBooks.Clear();
        AvailableBooks.Add(new FilterOption(AllBooksToken, Loc.Tr("Writing_FilterAll")));
        foreach (var b in books) AvailableBooks.Add(new FilterOption(b!, b!));
        SelectedBook = books.Contains(prevBook) ? prevBook : AllBooksToken;
        OnPropertyChanged(nameof(SelectedBook));

        // Chart types: only those that actually appear (excluding None).
        var chartTypes = TestSets
            .Select(t => t.Task1ChartType)
            .Where(t => t != WritingChartType.None)
            .Distinct()
            .OrderBy(t => FormatChartType(t))
            .ToList();
        var prevChartValue = SelectedChartType?.Value;
        AvailableChartTypes.Clear();
        AvailableChartTypes.Add(new ChartTypeOption(Loc.Tr("Writing_FilterAll"), null));
        foreach (var ct in chartTypes) AvailableChartTypes.Add(new ChartTypeOption(FormatChartType(ct), ct));
        SelectedChartType =
            AvailableChartTypes.FirstOrDefault(o => o.Value == prevChartValue)
            ?? AvailableChartTypes.First();
        OnPropertyChanged(nameof(SelectedChartType));
    }

    partial void OnSelectedBookChanged(string value) => RefreshView();
    partial void OnSelectedChartTypeChanged(ChartTypeOption? value) => RefreshView();
    partial void OnSelectedCompletionChanged(string value) => RefreshView();

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedBook = AllBooksToken;
        SelectedChartType = AvailableChartTypes.FirstOrDefault();
        SelectedCompletion = CompletionAll;
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
            IsContentMissing = !_content.IsImported(ContentSection.Writing);
            if (IsContentMissing)
            {
                TestSets.Clear();
                RecentAttempts.Clear();
                RefreshView();
                return;
            }

            var sets = await _taskSvc.ListTestSetsAsync();
            TestSets.Clear();
            foreach (var s in sets) TestSets.Add(s);

            RebuildFilterOptions();
            RefreshView();

            var history = await _taskSvc.ListAttemptsAsync(30);
            RecentAttempts.Clear();
            foreach (var a in history) RecentAttempts.Add(a);

            if (TestSets.Count == 0)
            {
                StatusText = Loc.Tr("Writing_NoTestsLoaded");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load writing hub data");
            StatusText = Loc.Tr("Writing_LoadTestsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedTestSetChanged(WritingTestSetSummary? value)
    {
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (SelectedTestSet is null) return;

        var sessionVm = _services.GetRequiredService<WritingSessionViewModel>();
        sessionVm.Submitted += OnSessionSubmitted;
        sessionVm.Cancelled += OnSessionCancelled;

        await sessionVm.StartAsync(SelectedTestSet.Id);

        var window = new WritingSessionWindow
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

    private bool CanStart() => SelectedTestSet is not null;

    private async void OnSessionCancelled(IReadOnlyList<int> attemptIds)
    {
        _sessionWindow?.Close();
        try
        {
            foreach (var id in attemptIds)
                await _taskSvc.DeleteAttemptAsync(id);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to discard cancelled attempts");
        }
        await LoadAsync();
    }

    private async void OnSessionSubmitted(IReadOnlyList<int> attemptIds)
    {
        _sessionWindow?.Close();

        _processingVm = new AiProcessingViewModel();
        _processingVm.Start();
        _processingWindow = new AiProcessingWindow
        {
            DataContext = _processingVm,
            Owner = Application.Current.MainWindow
        };
        _processingWindow.Show();

        try
        {
            for (var i = 0; i < attemptIds.Count; i++)
            {
                _processingVm.StatusText = Loc.Format("Writing_EvalTaskProgress", i + 1, attemptIds.Count);
                await _feedback.EvaluateAndSaveAsync(attemptIds[i]);
            }
            _processingVm.StatusText = Loc.Tr("Writing_EvalDoneOpeningResult");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Writing evaluator failed");
            _processingVm.StatusText = Loc.Tr("Writing_EvalPartialFail") + ex.Message;
            await Task.Delay(2000);
        }
        finally
        {
            _processingVm?.Stop();
            _processingWindow?.Close();
            _processingWindow = null;
            _processingVm = null;
        }

        await ShowResultAsync(attemptIds);
    }

    [RelayCommand]
    private async Task OpenAttemptAsync(WritingAttemptSummary? summary)
    {
        if (summary is null) return;
        await ShowResultAsync(new[] { summary.Id });
    }

    [RelayCommand]
    private async Task ShowHistoryAsync()
    {
        var historyVm = _services.GetRequiredService<WritingHistoryViewModel>();
        historyVm.BackRequested += OnHistoryBack;
        historyVm.OpenAttemptRequested += OnHistoryOpenAttempt;

        await historyVm.LoadAsync();

        CurrentScreen = historyVm;
        OnPropertyChanged(nameof(IsHubVisible));
    }

    private async void OnHistoryBack()
    {
        if (CurrentScreen is WritingHistoryViewModel old)
        {
            old.BackRequested -= OnHistoryBack;
            old.OpenAttemptRequested -= OnHistoryOpenAttempt;
        }
        CurrentScreen = null;
        OnPropertyChanged(nameof(IsHubVisible));
        await LoadAsync();
    }

    private async void OnHistoryOpenAttempt(int attemptId)
    {
        if (CurrentScreen is WritingHistoryViewModel old)
        {
            old.BackRequested -= OnHistoryBack;
            old.OpenAttemptRequested -= OnHistoryOpenAttempt;
        }
        await ShowResultAsync(new[] { attemptId });
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirm = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("Writing_ClearHistoryTitle"),
            Loc.Tr("Writing_ClearHistoryBody"),
            confirmText: Loc.Tr("Writing_ClearHistoryConfirm"));

        if (!confirm) return;

        try
        {
            var removed = await _taskSvc.ClearHistoryAsync();
            StatusText = removed == 0
                ? Loc.Tr("Writing_HistoryAlreadyEmpty")
                : Loc.Format("Writing_HistoryCleared", removed);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear writing history");
            StatusText = Loc.Tr("Writing_ClearHistoryFailed") + ex.Message;
        }

        await LoadAsync();
    }

    private async Task ShowResultAsync(IReadOnlyList<int> attemptIds)
    {
        var resultVm = _services.GetRequiredService<WritingResultViewModel>();
        await resultVm.LoadAsync(attemptIds);

        var holder = new WritingResultScreen(resultVm, async () =>
        {
            CurrentScreen = null;
            await LoadAsync();
            OnPropertyChanged(nameof(IsHubVisible));
        });
        CurrentScreen = holder;
        OnPropertyChanged(nameof(IsHubVisible));
    }
}

public sealed partial class WritingResultScreen : ObservableObject
{
    private readonly Func<Task> _onBack;

    public WritingResultViewModel Result { get; }

    public WritingResultScreen(WritingResultViewModel result, Func<Task> onBack)
    {
        Result = result;
        _onBack = onBack;
    }

    [RelayCommand]
    private async Task BackAsync() => await _onBack();
}
