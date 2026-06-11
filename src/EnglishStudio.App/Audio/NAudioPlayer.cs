using System.IO;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.Audio;

public sealed class NAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly ILogger<NAudioPlayer> _logger;
    private readonly object _gate = new();

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public NAudioPlayer(ILogger<NAudioPlayer> logger)
    {
        _logger = logger;
    }

    public void Play(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Audio file not found: {Path}", filePath);
            return;
        }

        lock (_gate)
        {
            DisposeOutputs();

            try
            {
                _reader = new AudioFileReader(filePath);
                _output = new WaveOutEvent();
                _output.Init(_reader);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play audio file {Path}", filePath);
                DisposeOutputs();
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            DisposeOutputs();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output)) return;
            DisposeOutputs();
        }
    }

    private void DisposeOutputs()
    {
        if (_output is not null)
        {
            try
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                _output.Stop();
                _output.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing WaveOutEvent");
            }
            _output = null;
        }

        if (_reader is not null)
        {
            try { _reader.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error disposing AudioFileReader"); }
            _reader = null;
        }
    }

    public void Dispose() => Stop();
}
