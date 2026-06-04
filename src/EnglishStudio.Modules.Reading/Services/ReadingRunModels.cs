namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Lifecycle of a live read-along session.</summary>
public enum ReadAlongState
{
    Idle,
    LoadingModel,
    Listening,
    Finished,
    Error
}

/// <summary>
/// Live tracking progress. <see cref="CursorWordIndex"/> is in the same word-index space as
/// <see cref="TextToken.WordIndex"/>, so the UI can dim every word before the cursor.
/// </summary>
public sealed record ReadAlongProgress(int CursorWordIndex, int WordsRead, double Wpm, double ElapsedSec);

/// <summary>Result of one reading attempt, returned by Stop (before the heavier Whisper analysis).</summary>
public sealed record ReadingRunResult(int WordsRead, double Wpm, double ElapsedSec, bool Completed, string? WavPath);

/// <summary>Per-word outcome from the post-read analysis.</summary>
public sealed record ReadingWordOutcome(int TokenIndex, bool Spoken, bool Skipped, bool Mispronounced, double? Score);

/// <summary>Aggregate of the post-read Whisper analysis.</summary>
public sealed record ReadingAnalysis(
    double AccuracyPct,
    int WordsExpected,
    int WordsSpoken,
    int WordsSkipped,
    IReadOnlyList<ReadingWordOutcome> Words,
    IReadOnlyList<int> DifficultWordIndices);

/// <summary>Persisted reading session, for history and trends.</summary>
public sealed record ReadingSessionSummary(
    int Id,
    int ReadingTextId,
    DateTime StartedAt,
    int DurationSec,
    int WordsRead,
    double Wpm,
    double AccuracyPct,
    bool Completed);
