namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Post-read analysis of a recorded WAV against the reference text (Whisper word-timestamps +
/// alignment). Implemented by the engine (Agent A). Returns null if analysis can't run
/// (e.g. model unavailable).
/// </summary>
public interface IReadingAnalysisService
{
    Task<ReadingAnalysis?> AnalyzeAsync(
        string wavPath,
        IReadOnlyList<TextToken> tokens,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
