using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.Audio;

public sealed class ListeningAudioPlayer : IListeningAudioPlayer
{
    private readonly ILogger<ListeningAudioPlayer> _logger;
    private readonly object _gate = new();
    private readonly DispatcherTimer _tick;

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private string? _loadedPath;
    private bool _suppressEndedEvent;
    private float _volume = 1f;

    public ListeningAudioPlayer(ILogger<ListeningAudioPlayer> logger)
    {
        _logger = logger;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => PositionChanged?.Invoke();
    }

    public event Action? PositionChanged;
    public event Action? PlaybackEnded;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            lock (_gate)
            {
                if (_reader is not null) _reader.Volume = _volume;
            }
        }
    }

    public void Load(string filePath)
    {
        lock (_gate)
        {
            if (string.Equals(_loadedPath, filePath, StringComparison.OrdinalIgnoreCase) && _reader is not null)
                return;

            DisposeOutputs();

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _logger.LogWarning("Listening audio file not found: {Path}", filePath);
                _loadedPath = null;
                return;
            }

            try
            {
                _reader = new AudioFileReader(filePath) { Volume = _volume };
                _output = new WaveOutEvent();
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _loadedPath = filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load audio file {Path}", filePath);
                DisposeOutputs();
                _loadedPath = null;
            }
        }
        PositionChanged?.Invoke();
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_output is null || _reader is null) return;
            _suppressEndedEvent = false;
            // Restart from the beginning if we previously ran to the end.
            if (_reader.CurrentTime >= _reader.TotalTime) _reader.CurrentTime = TimeSpan.Zero;
            _output.Play();
        }
        _tick.Start();
        PositionChanged?.Invoke();
    }

    public void Pause()
    {
        lock (_gate)
        {
            _output?.Pause();
        }
        _tick.Stop();
        PositionChanged?.Invoke();
    }

    public void Stop()
    {
        _tick.Stop();
        lock (_gate)
        {
            if (_output is not null)
            {
                _suppressEndedEvent = true;
                _output.Stop();
            }
            if (_reader is not null) _reader.CurrentTime = TimeSpan.Zero;
        }
        PositionChanged?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_reader is null) return;
            var clamped = position < TimeSpan.Zero ? TimeSpan.Zero
                : position > _reader.TotalTime ? _reader.TotalTime
                : position;
            _reader.CurrentTime = clamped;
        }
        PositionChanged?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool ended;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output)) return;
            _tick.Stop();
            // Reaching the natural end: position is at (or near) total time. Distinguish from an
            // explicit Stop() (which suppresses) so we only fire PlaybackEnded for true completion.
            var suppress = _suppressEndedEvent;
            _suppressEndedEvent = false;
            ended = !suppress && _reader is not null && _reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(300);
        }
        PositionChanged?.Invoke();
        if (ended) PlaybackEnded?.Invoke();
    }

    private void DisposeOutputs()
    {
        if (_output is not null)
        {
            try
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _suppressEndedEvent = true;
                _output.Stop();
                _output.Dispose();
            }
            catch (Exception ex) { _logger.LogDebug(ex, "Error disposing WaveOutEvent"); }
            _output = null;
        }
        if (_reader is not null)
        {
            try { _reader.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error disposing AudioFileReader"); }
            _reader = null;
        }
    }

    public void Dispose()
    {
        _tick.Stop();
        lock (_gate) DisposeOutputs();
    }
}
