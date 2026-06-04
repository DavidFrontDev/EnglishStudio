namespace EnglishStudio.App.Reading.Audio;

/// <summary>
/// One chunk of captured microphone audio: raw 16 kHz mono PCM-16 little-endian bytes.
/// <see cref="Buffer"/> is a fresh copy owned by the handler — safe to keep or pass to Vosk.
/// </summary>
public readonly record struct ReadingPcmFrame(byte[] Buffer, int Count);

/// <summary>
/// Dedicated microphone capture for the reading module. Separate from
/// <c>NAudioRecorder</c> (which is shared with IELTS Speaking/M6 and must not change):
/// this one both writes a WAV (for the Phase-4 Whisper analysis) and streams live PCM
/// frames for the Vosk read-along cursor.
/// </summary>
public interface IReadingAudioCapture : IDisposable
{
    bool IsCapturing { get; }

    /// <summary>Path of the WAV being / last written, or null before the first capture.</summary>
    string? LastWavPath { get; }

    bool IsMicrophoneAvailable();

    /// <summary>Raised on a background thread for every captured buffer while capturing.</summary>
    event EventHandler<ReadingPcmFrame>? FrameAvailable;

    /// <summary>Starts capture, writing a new WAV under %AppData%/EnglishStudio/Reading/.</summary>
    void Start();

    /// <summary>Stops capture and finalizes the WAV. Returns its path (or null on failure).</summary>
    string? Stop();
}
