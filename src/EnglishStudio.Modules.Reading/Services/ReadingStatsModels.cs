namespace EnglishStudio.Modules.Reading.Services;

/// <summary>One point of the reading-speed trend (a completed session).</summary>
public sealed record ReadingSpeedPoint(DateTime Date, double Wpm, double AccuracyPct);

/// <summary>
/// Vocabulary coverage of a text: how many distinct words are "known" (in SRS or a known CEFR).
/// </summary>
public sealed record VocabCoverage(int TextId, string Title, int TotalWords, int KnownWords, double CoveragePct);

/// <summary>Aggregate reading stats for the progress screen.</summary>
public sealed record ReadingStatsSummary(
    int SessionsTotal,
    int WordsReadTotal,
    double AvgWpm,
    double BestWpm,
    int MinutesReadTotal,
    IReadOnlyList<ReadingSpeedPoint> SpeedTrend);
