namespace EnglishStudio.App.Audio;

public enum WhisperModelSize
{
    /// <summary>~142 MB, used by M6 pronunciation training.</summary>
    Base,
    /// <summary>~1.5 GB, used by M10 Speaking for higher transcript accuracy + word timestamps.</summary>
    Medium
}

public sealed record WordTimestamp(string Word, double StartSec, double EndSec);

public sealed record WhisperTranscriptResult(string Text, IReadOnlyList<WordTimestamp> Words);

public interface IWhisperTranscriber
{
    /// <summary>True once at least one model is loaded.</summary>
    bool IsModelReady { get; }

    /// <summary>Default model used by the legacy <see cref="TranscribeAsync"/>.</summary>
    WhisperModelSize CurrentModelSize { get; }

    /// <summary>
    /// Ensures the requested model is on disk and the factory is initialised. Idempotent —
    /// callers can ask for Base and Medium independently; both end up in
    /// %AppData%/EnglishStudio/Models/.
    /// </summary>
    Task<bool> EnsureModelDownloadedAsync(
        WhisperModelSize size,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    /// <summary>Legacy alias for the Base model — kept for M6 pronunciation flow.</summary>
    Task<bool> EnsureModelDownloadedAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Transcribe using the Base model (legacy path).</summary>
    Task<string?> TranscribeAsync(string wavPath, CancellationToken ct = default);

    /// <summary>
    /// Transcribe with word-level timestamps using the Medium model. Used by Speaking metrics.
    /// Returns null if the model isn't ready or the file can't be opened.
    /// <paramref name="progress"/> reports a 0..1 fraction of the clip transcribed so far
    /// (based on processed audio position), ending at 1.0 on completion.
    /// </summary>
    Task<WhisperTranscriptResult?> TranscribeWithTimestampsAsync(
        string wavPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
