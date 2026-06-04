using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Srs;

namespace EnglishStudio.App.ViewModels;

public partial class StatsViewModel : ObservableObject
{
    private readonly ISrsService _srs;

    [ObservableProperty]
    private SrsStats? _stats;

    [ObservableProperty]
    private string _retentionLabel = "—";

    public StatsViewModel(ISrsService srs)
    {
        _srs = srs;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Stats = await _srs.GetStatsAsync(DateTime.UtcNow);
        RetentionLabel = Stats is null
            ? "—"
            : $"{Stats.RetentionRate30d * 100:0.0}%";
    }
}
