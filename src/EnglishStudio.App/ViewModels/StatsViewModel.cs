using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Srs;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly ISrsService _srs;
    private readonly ILogger<StatsViewModel> _log;

    [ObservableProperty]
    private SrsStats? _stats;

    [ObservableProperty]
    private string _retentionLabel = "—";

    public StatsViewModel(ISrsService srs, ILogger<StatsViewModel> log)
    {
        _srs = srs;
        _log = log;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            Stats = await _srs.GetStatsAsync(DateTime.UtcNow);
            RetentionLabel = Stats is null
                ? "—"
                : $"{Stats.RetentionRate30d * 100:0.0}%";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to refresh SRS stats");
        }
    }
}
