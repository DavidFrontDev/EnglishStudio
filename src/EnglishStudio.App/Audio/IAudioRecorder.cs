namespace EnglishStudio.App.Audio;

public interface IAudioRecorder
{
    bool IsRecording { get; }
    void StartRecording();

    /// <summary>
    /// Stops recording and returns the path to the saved WAV file (16 kHz mono PCM).
    /// </summary>
    string? StopRecording();

    bool IsMicrophoneAvailable();
}
