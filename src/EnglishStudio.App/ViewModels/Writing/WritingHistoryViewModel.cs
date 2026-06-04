using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Writing;

/// <summary>
/// Backs the "Подробная история" sub-screen launched from <see cref="WritingHubViewModel"/>.
/// Computes summary stats and a weekly band-trend on top of the raw history entries.
/// </summary>
public partial class WritingHistoryViewModel : ObservableObject
{
    private readonly IWritingTaskService _taskSvc;
    private readonly IServiceProvider _services;
    private readonly ILogger<WritingHistoryViewModel> _log;

    public ObservableCollection<WritingHistoryEntry> Entries { get; } = new();
    public ObservableCollection<BandTrendPoint> TrendPoints { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorText;

    // Stats
    [ObservableProperty] private int _totalAttempts;
    [ObservableProperty] private int _completedAttempts;
    [ObservableProperty] private int _totalWords;
    [ObservableProperty] private double? _averageBand;
    [ObservableProperty] private double? _bestBand;
    [ObservableProperty] private TimeSpan _totalTimeSpent;
    [ObservableProperty] private DateTime? _firstAttemptAt;
    [ObservableProperty] private DateTime? _lastAttemptAt;

    public string TotalTimeSpentDisplay =>
        TotalTimeSpent.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.#} ч", TotalTimeSpent.TotalHours)
            : string.Format(CultureInfo.InvariantCulture, "{0:0} мин", TotalTimeSpent.TotalMinutes);

    /// <summary>Raised when the user clicks "Открыть" on an entry — host shows the result screen.</summary>
    public event Action<int>? OpenAttemptRequested;
    /// <summary>Raised when the user clicks the back button.</summary>
    public event Action? BackRequested;

    public WritingHistoryViewModel(
        IWritingTaskService taskSvc,
        IServiceProvider services,
        ILogger<WritingHistoryViewModel> log)
    {
        _taskSvc = taskSvc;
        _services = services;
        _log = log;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            var entries = await _taskSvc.ListHistoryAsync(500);
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            RecomputeStats();
            RecomputeTrend();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load writing history");
            ErrorText = "Не удалось загрузить историю: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RecomputeStats()
    {
        TotalAttempts = Entries.Count;
        var completed = Entries.Where(e => e.SubmittedAt is not null).ToList();
        CompletedAttempts = completed.Count;
        TotalWords = completed.Sum(e => e.WordCount);
        TotalTimeSpent = TimeSpan.FromSeconds(completed.Sum(e => (long)e.DurationSeconds));

        var bands = completed
            .Where(e => e.BandOverall is not null)
            .Select(e => e.BandOverall!.Value)
            .ToList();
        AverageBand = bands.Count == 0 ? null : Math.Round(bands.Average(), 1);
        BestBand = bands.Count == 0 ? null : bands.Max();

        FirstAttemptAt = Entries.Count == 0 ? null : Entries.Min(e => e.StartedAt);
        LastAttemptAt  = Entries.Count == 0 ? null : Entries.Max(e => e.StartedAt);

        OnPropertyChanged(nameof(TotalTimeSpentDisplay));
    }

    /// <summary>
    /// Bucket completed attempts into ISO-week groups (Monday-start) and emit one point per week
    /// with the average band. Weeks without completed attempts are skipped — the chart connects
    /// the existing dots so a sparse history still reads as a trend.
    /// </summary>
    private void RecomputeTrend()
    {
        TrendPoints.Clear();
        var completed = Entries
            .Where(e => e.SubmittedAt is not null && e.BandOverall is not null)
            .ToList();
        if (completed.Count == 0) return;

        var grouped = completed
            .GroupBy(e => StartOfIsoWeek(e.SubmittedAt!.Value.Date))
            .OrderBy(g => g.Key);

        foreach (var g in grouped)
        {
            var bands = g.Select(x => x.BandOverall!.Value).ToList();
            var point = new BandTrendPoint(
                WeekStart: g.Key,
                AverageBand: Math.Round(bands.Average(), 2),
                AttemptCount: bands.Count);
            TrendPoints.Add(point);
        }
    }

    private static DateTime StartOfIsoWeek(DateTime date)
    {
        // ISO week starts on Monday. Sunday=0 in DayOfWeek, shift so Monday=0.
        var delta = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-delta);
    }

    [RelayCommand]
    private void OpenAttempt(WritingHistoryEntry? entry)
    {
        if (entry is null) return;
        OpenAttemptRequested?.Invoke(entry.AttemptId);
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke();

    public sealed record BandTrendPoint(DateTime WeekStart, double AverageBand, int AttemptCount);
}
