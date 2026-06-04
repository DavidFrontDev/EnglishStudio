namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Orchestrates a live read-along session: microphone capture → streaming ASR (Vosk) →
/// forced-alignment cursor over the known reference text. Implemented by the engine
/// (Agent A); consumed by the reader UI (Agent B).
/// </summary>
/// <remarks>
/// All events are raised on the UI thread — the implementation captures the
/// <see cref="System.Threading.SynchronizationContext"/> in <see cref="StartAsync"/> and
/// marshals callbacks itself. Consumers must NOT marshal again.
/// </remarks>
public interface IReadAlongController
{
    ReadAlongState State { get; }

    /// <summary>True once the speech model is downloaded and loaded.</summary>
    bool IsModelReady { get; }

    event EventHandler<ReadAlongState>? StateChanged;

    /// <summary>Human-readable model download/loading status (first run only).</summary>
    event EventHandler<string>? ModelDownloadStatus;

    /// <summary>Cursor / WPM updates while listening (throttled).</summary>
    event EventHandler<ReadAlongProgress>? Progress;

    /// <summary>Raised when the session ends (via <see cref="StopAsync"/> or end of text).</summary>
    event EventHandler<ReadingRunResult>? Finished;

    /// <summary>Ensures the speech model is on disk and loaded. Idempotent.</summary>
    Task<bool> EnsureModelAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Starts capture + tracking against the reference tokens.</summary>
    Task StartAsync(IReadOnlyList<TextToken> referenceTokens, CancellationToken ct = default);

    /// <summary>Stops capture and returns the run result (including the WAV path for analysis).</summary>
    Task<ReadingRunResult> StopAsync();
}
