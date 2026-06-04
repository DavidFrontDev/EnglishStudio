using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Content;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.App.Views.Writing;
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
    private readonly IServiceProvider _services;
    private readonly ILogger<WritingHubViewModel> _log;

    private WritingSessionWindow? _sessionWindow;
    private AiProcessingWindow? _processingWindow;
    private AiProcessingViewModel? _processingVm;

    // Token used in filter ComboBoxes to mean "no filter / show all".
    public const string AllFilterToken = "Все";

    public ObservableCollection<WritingTestSetSummary> TestSets { get; } = new();
    public ObservableCollection<WritingAttemptSummary> RecentAttempts { get; } = new();

    /// <summary>Live, filterable view over <see cref="TestSets"/>. Bound to the ListBox.</summary>
    public ICollectionView TestSetsView { get; }

    public ObservableCollection<string> AvailableBooks { get; } = new();
    public ObservableCollection<ChartTypeOption> AvailableChartTypes { get; } = new();
    public ObservableCollection<string> AvailableCompletionFilters { get; } = new()
    {
        AllFilterToken, "Не пройденные", "Пройденные"
    };

    [ObservableProperty] private string _selectedBook = AllFilterToken;
    [ObservableProperty] private ChartTypeOption? _selectedChartType;
    [ObservableProperty] private string _selectedCompletion = AllFilterToken;

    [ObservableProperty] private WritingTestSetSummary? _selectedTestSet;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private int _visibleCount;

    /// <summary>Currently displayed sub-screen: null = hub, otherwise a child VM (result screen).</summary>
    [ObservableProperty] private object? _currentScreen;

    /// <summary>True when the Writing content pack isn't imported — shows the gating banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText =
        "Задания IELTS Writing входят в контент-пак. Импортируйте пак, чтобы открыть раздел.";

    public bool IsHubVisible => CurrentScreen is null;

    public WritingHubViewModel(
        IWritingTaskService taskSvc,
        WritingFeedbackService feedback,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IServiceProvider services,
        ILogger<WritingHubViewModel> log)
    {
        _taskSvc = taskSvc;
        _feedback = feedback;
        _content = content;
        _importLauncher = importLauncher;
        _services = services;
        _log = log;
        CurrentScreen = null;

        // Wrap the TestSets ObservableCollection in a live, filterable view so changing
        // a filter ComboBox refreshes the list without rebuilding the underlying data.
        TestSetsView = CollectionViewSource.GetDefaultView(TestSets);
        TestSetsView.Filter = MatchesFilter;

        // Seed the chart-type ComboBox with "Все" + all enum values that actually appear in seed.
        // (Populated once here so the dropdown is non-empty before the first load completes.)
        AvailableBooks.Add(AllFilterToken);

        _ = LoadAsync();
    }

    /// <summary>Item displayed in the chart-type filter ComboBox.</summary>
    public sealed record ChartTypeOption(string Label, WritingChartType? Value)
    {
        public override string ToString() => Label;
    }

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
        if (SelectedBook != AllFilterToken)
        {
            var book = ExtractBookNumber(s.Title);
            if (book != SelectedBook) return false;
        }

        // Chart type filter.
        if (SelectedChartType?.Value is { } ct && s.Task1ChartType != ct) return false;

        // Completion filter.
        if (SelectedCompletion == "Не пройденные" && s.CompletedAttempts > 0) return false;
        if (SelectedCompletion == "Пройденные" && s.CompletedAttempts == 0) return false;

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

    private void RebuildFilterOptions()
    {
        // Books: derive from TestSet titles. Numeric sort, "Все" stays on top.
        var books = TestSets
            .Select(t => ExtractBookNumber(t.Title))
            .Where(b => b is not null)
            .Distinct()
            .OrderBy(b => int.Parse(b!))
            .ToList();
        var prevBook = SelectedBook;
        AvailableBooks.Clear();
        AvailableBooks.Add(AllFilterToken);
        foreach (var b in books) AvailableBooks.Add(b!);
        SelectedBook = AvailableBooks.Contains(prevBook) ? prevBook : AllFilterToken;

        // Chart types: only those that actually appear (excluding None).
        var chartTypes = TestSets
            .Select(t => t.Task1ChartType)
            .Where(t => t != WritingChartType.None)
            .Distinct()
            .OrderBy(t => FormatChartType(t))
            .ToList();
        var prevChartLabel = SelectedChartType?.Label;
        AvailableChartTypes.Clear();
        AvailableChartTypes.Add(new ChartTypeOption(AllFilterToken, null));
        foreach (var ct in chartTypes) AvailableChartTypes.Add(new ChartTypeOption(FormatChartType(ct), ct));
        SelectedChartType =
            AvailableChartTypes.FirstOrDefault(o => o.Label == prevChartLabel)
            ?? AvailableChartTypes.First();
    }

    partial void OnSelectedBookChanged(string value) => RefreshView();
    partial void OnSelectedChartTypeChanged(ChartTypeOption? value) => RefreshView();
    partial void OnSelectedCompletionChanged(string value) => RefreshView();

    [RelayCommand]
    private void ResetFilters()
    {
        SelectedBook = AllFilterToken;
        SelectedChartType = AvailableChartTypes.FirstOrDefault();
        SelectedCompletion = AllFilterToken;
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
                StatusText = "Тесты ещё не загружены. Перезапустите приложение для импорта seed.";
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load writing hub data");
            StatusText = "Не удалось загрузить список тестов.";
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

        _sessionWindow = new WritingSessionWindow
        {
            DataContext = sessionVm,
            Owner = Application.Current.MainWindow
        };
        _sessionWindow.Closed += (_, _) =>
        {
            sessionVm.Submitted -= OnSessionSubmitted;
            sessionVm.Cancelled -= OnSessionCancelled;
            _sessionWindow = null;
        };
        _sessionWindow.Show();
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
                _processingVm.StatusText = $"Оценка Task {i + 1} из {attemptIds.Count}…";
                await _feedback.EvaluateAndSaveAsync(attemptIds[i]);
            }
            _processingVm.StatusText = "Готово. Открываем результат…";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Writing evaluator failed");
            _processingVm.StatusText = "AI-оценка частично не сработала: " + ex.Message;
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
            "Очистить историю",
            "Удалить все записи о пройденных Writing-тестах? Это действие необратимо.",
            confirmText: "Очистить");

        if (!confirm) return;

        try
        {
            var removed = await _taskSvc.ClearHistoryAsync();
            StatusText = removed == 0
                ? "История уже была пуста."
                : $"Удалено записей: {removed}.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear writing history");
            StatusText = "Не удалось очистить историю: " + ex.Message;
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
