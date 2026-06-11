using System.IO;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Images;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.Audio;

public sealed class NAudioRecorder : IAudioRecorder, IDisposable
{
    private readonly ILogger<NAudioRecorder> _logger;
    private readonly IAppSettings _settings;
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentPath;
    private ManualResetEventSlim? _stoppedSignal;

    public NAudioRecorder(ILogger<NAudioRecorder> logger, IAppSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public bool IsRecording { get; private set; }

    public bool IsMicrophoneAvailable()
    {
        try { return WaveInEvent.DeviceCount > 0; }
        catch { return false; }
    }

    public void StartRecording()
    {
        lock (_gate)
        {
            if (IsRecording) return;
            if (!IsMicrophoneAvailable())
            {
                _logger.LogWarning("No microphone available.");
                return;
            }

            try
            {
                var dir = Path.Combine(DictionaryPaths.AppDataRoot, "Pronunciation");
                Directory.CreateDirectory(dir);
                _currentPath = Path.Combine(dir, $"rec_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.wav");

                // 16 kHz mono PCM — Whisper requirement
                var fmt = new WaveFormat(16000, 16, 1);
                _waveIn = CreateWaveIn(fmt, ResolveDeviceNumber());
                _writer = new WaveFileWriter(_currentPath, fmt);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _stoppedSignal?.Dispose();
                _stoppedSignal = new ManualResetEventSlim(false);
                _waveIn.StartRecording();
                IsRecording = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start recording");
                DisposeInternals();
            }
        }
    }

    public string? StopRecording()
    {
        lock (_gate)
        {
            if (!IsRecording) return null;
            try
            {
                _waveIn?.StopRecording();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping recording");
            }

            try { _stoppedSignal?.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { _logger.LogDebug(ex, "waiting for recording stop"); }

            // Finalize writer & dispose
            try { _writer?.Flush(); }
            catch (Exception ex) { _logger.LogDebug(ex, "writer flush"); }

            var path = _currentPath;
            DisposeInternals();
            IsRecording = false;
            return path;
        }
    }

    /// <summary>
    /// WaveInEvent posts RecordingStopped to the SynchronizationContext captured at construction;
    /// construct without one so the stop signal fires on the capture thread rather than the
    /// dispatcher that StopRecording is blocking.
    /// </summary>
    private static WaveInEvent CreateWaveIn(WaveFormat fmt, int deviceNumber)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { return new WaveInEvent { WaveFormat = fmt, DeviceNumber = deviceNumber }; }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }
    }

    /// <summary>
    /// Maps the device name saved in settings to a live <c>WaveInEvent</c> index. Indices are not
    /// stable across reboots / re-plugging, so we match by product name and fall back to -1
    /// (the Windows default recording device) when the saved device is absent or unset.
    /// </summary>
    private int ResolveDeviceNumber()
    {
        var name = _settings.MicrophoneDeviceName;
        if (string.IsNullOrWhiteSpace(name)) return -1;

        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                if (string.Equals(WaveInEvent.GetCapabilities(i).ProductName, name, StringComparison.Ordinal))
                    return i;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Resolving microphone device number failed");
        }

        _logger.LogInformation("Saved microphone '{Name}' not found; using system default.", name);
        return -1;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DataAvailable write error");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stoppedSignal?.Set();
        if (e.Exception is not null)
            _logger.LogWarning(e.Exception, "Recording stopped with exception");
    }

    private void DisposeInternals()
    {
        if (_waveIn is not null)
        {
            try
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
            }
            catch (Exception ex) { _logger.LogDebug(ex, "disposing WaveInEvent"); }
            _waveIn = null;
        }
        if (_writer is not null)
        {
            try { _writer.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "disposing WaveFileWriter"); }
            _writer = null;
        }
        _currentPath = null;
    }

    public void Dispose() => StopRecording();
}
