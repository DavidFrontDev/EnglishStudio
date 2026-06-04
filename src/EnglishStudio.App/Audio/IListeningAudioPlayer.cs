namespace EnglishStudio.App.Audio;

/// <summary>
/// Richer audio player for the Listening section: load/play/pause/seek with position + duration
/// reporting. Kept separate from <see cref="IAudioPlayer"/> (fire-and-forget Play/Stop) so the
/// dictionary/pronunciation features stay untouched.
/// </summary>
public interface IListeningAudioPlayer : IDisposable
{
    /// <summary>Loads a file but does not start playback. No-op if the path is the same as currently loaded.</summary>
    void Load(string filePath);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    bool IsPlaying { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }

    /// <summary>Playback volume, 0.0–1.0. Retained across <see cref="Load"/> calls.</summary>
    float Volume { get; set; }

    /// <summary>Fires ~5×/sec while playing (and once on Pause/Seek) with the current position.</summary>
    event Action? PositionChanged;

    /// <summary>Fires when playback reaches the end of the file.</summary>
    event Action? PlaybackEnded;
}
