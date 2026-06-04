namespace EnglishStudio.App.Audio;

/// <summary>
/// A selectable audio input device. <see cref="DeviceNumber"/> maps to NAudio's
/// <c>WaveInEvent.DeviceNumber</c>; <c>-1</c> means the Windows default recording device.
/// </summary>
public sealed record MicrophoneDeviceInfo(int DeviceNumber, string Name)
{
    public bool IsSystemDefault => DeviceNumber < 0;
}
