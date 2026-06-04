namespace EnglishStudio.App.Audio;

/// <summary>
/// Lets the user verify a microphone before committing to it: enumerates input devices,
/// streams a live input level (for a VU meter) and optionally captures a short clip that
/// can be played back. Independent from <see cref="IAudioRecorder"/> so a test can run on
/// the device currently highlighted in the settings dropdown, before it is saved.
/// </summary>
public interface IMicrophoneTester : IDisposable
{
    /// <summary>True while the device is open and streaming levels.</summary>
    bool IsActive { get; }

    /// <summary>Raised on the UI thread with the current peak level, normalised 0..1.</summary>
    event EventHandler<double>? LevelChanged;

    /// <summary>Enumerates input devices. The first entry is always the system default (DeviceNumber -1).</summary>
    IReadOnlyList<MicrophoneDeviceInfo> GetDevices();

    /// <summary>
    /// Opens the given device and starts streaming levels. When <paramref name="record"/> is
    /// true the captured audio is also written to a reusable WAV file (16 kHz mono PCM — the
    /// exact format the pronunciation/Speaking pipeline uses).
    /// </summary>
    void Start(int deviceNumber, bool record);

    /// <summary>Stops the device. Returns the path to the recorded WAV when recording was on, else null.</summary>
    string? Stop();
}
