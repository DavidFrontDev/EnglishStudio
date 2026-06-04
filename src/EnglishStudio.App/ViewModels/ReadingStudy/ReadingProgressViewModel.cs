using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// "📈 Прогресс" screen (F6): aggregate reading stats — speed trend over time and per-text
/// vocabulary coverage. Built fresh per open (transient). Separate from the global "Статистика".
/// </summary>
public partial class ReadingProgressViewModel : ObservableObject
{
    private readonly IReadingStatsService _stats;
    private readonly ILogger<ReadingProgressViewModel> _log;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusText;

    [ObservableProperty] private int _sessionsTotal;
    [ObservableProperty] private int _wordsReadTotal;
    [ObservableProperty] private double _avgWpm;
    [ObservableProperty] private double _bestWpm;
    [ObservableProperty] private int _minutesReadTotal;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _hasCoverage;

    public ObservableCollection<ReadingSpeedPoint> SpeedTrend { get; } = new();
    public ObservableCollection<VocabCoverage> Coverage { get; } = new();

    /// <summary>Raised when the user dismisses the progress screen.</summary>
    public event Action? CloseRequested;

    public ReadingProgressViewModel(IReadingStatsService stats, ILogger<ReadingProgressViewModel> log)
    {
        _stats = stats;
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        StatusText = null;
        SpeedTrend.Clear();
        Coverage.Clear();

        try
        {
            var summary = await _stats.GetSummaryAsync(ct);
            SessionsTotal = summary.SessionsTotal;
            WordsReadTotal = summary.WordsReadTotal;
            AvgWpm = summary.AvgWpm;
            BestWpm = summary.BestWpm;
            MinutesReadTotal = summary.MinutesReadTotal;

            foreach (var p in summary.SpeedTrend) SpeedTrend.Add(p);

            var coverage = await _stats.GetCoverageAsync(ct);
            foreach (var c in coverage) Coverage.Add(c);

            HasData = SessionsTotal > 0;
            HasCoverage = Coverage.Count > 0;

            if (!HasData)
                StatusText = "Пока нет завершённых чтений вслух — статистика появится после первого.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load reading progress");
            StatusText = "Не удалось загрузить статистику чтения.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
