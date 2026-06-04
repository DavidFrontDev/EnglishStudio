namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Reading progress analytics (F6): speed trend over time and per-text vocabulary coverage,
/// aggregated from reading sessions + dictionary/SRS. Implemented by Agent A.
/// </summary>
public interface IReadingStatsService
{
    Task<ReadingStatsSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>Speed trend, optionally filtered to one text.</summary>
    Task<IReadOnlyList<ReadingSpeedPoint>> GetSpeedTrendAsync(int? textId = null, CancellationToken ct = default);

    Task<IReadOnlyList<VocabCoverage>> GetCoverageAsync(CancellationToken ct = default);
}
