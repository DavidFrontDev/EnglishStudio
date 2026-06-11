using System.IO;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Images;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.Reading.Audio;

/// <summary>
/// Reading-module microphone capture built on its own <see cref="WaveInEvent"/> (16 kHz mono
/// PCM-16 — the exact format both Vosk and Whisper expect, so no resampling). Writes the WAV
/// for the post-read analysis and raises <see cref="FrameAvailable"/> with a copied buffer for
/// the live Vosk cursor. Modeled on <c>NAudioRecorder</c> but kept separate so the shared
/// recorder (IELTS Speaking / M6) is untouched.
/// </summary>
public sealed class NAudioReadingCapture : IReadingAudioCapture
{
    private readonly ILogger<NAudioReadingCapture> _logger;
    private readonly IAppSettings _settings;
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private ManualResetEventSlim? _stoppedSignal;

    public NAudioReadingCapture(ILogger<NAudioReadingCapture> logger, IAppSettings settings)
    {
        _logger = logger;
        _settings = settings;
    }

    public bool IsCapturing { get; private set; }

    public string? LastWavPath { get; private set; }

    public event EventHandler<ReadingPcmFrame>? FrameAvailable;

    public bool IsMicrophoneAvailable()
    {
        try { return WaveInEvent.DeviceCount > 0; }
        catch { return false; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsCapturing) return;
            if (!IsMicrophoneAvailable())
            {
                _logger.LogWarning("No microphone available for reading capture.");
                return;
            }

            try
            {
                var dir = Path.Combine(DictionaryPaths.AppDataRoot, "Reading");
                Directory.CreateDirectory(dir);
                LastWavPath = Path.Combine(dir, $"rec_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.wav");

                var fmt = new WaveFormat(16000, 16, 1);
                _waveIn = CreateWaveIn(fmt, ResolveDeviceNumber());
                _writer = new WaveFileWriter(LastWavPath, fmt);

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;

                _stoppedSignal?.Dispose();
                _stoppedSignal = new ManualResetEventSlim(false);
                _waveIn.StartRecording();
                IsCapturing = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start reading capture");
                DisposeInternals();
            }
        }
    }

    public string? Stop()
    {
        lock (_gate)
        {
            if (!IsCapturing) return LastWavPath;
            try { _waveIn?.StopRecording(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping reading capture"); }

            try { _stoppedSignal?.Wait(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) { _logger.LogDebug(ex, "waiting for reading capture stop"); }

            try { _writer?.Flush(); }
            catch (Exception ex) { _logger.LogDebug(ex, "reading writer flush"); }

            var path = LastWavPath;
            DisposeInternals();
            IsCapturing = false;
            return path;
        }
    }

    /// <summary>
    /// WaveInEvent posts RecordingStopped to the SynchronizationContext captured at construction;
    /// construct without one so the stop signal fires on the capture thread rather than the
    /// dispatcher that Stop is blocking.
    /// </summary>
    private static WaveInEvent CreateWaveIn(WaveFormat fmt, int deviceNumber)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { return new WaveInEvent { WaveFormat = fmt, DeviceNumber = deviceNumber }; }
        finally { SynchronizationContext.SetSynchronizationContext(prev); }
    }

    /// <summary>
    /// Maps the device name saved in settings to a live <c>WaveInEvent</c> index, falling back
    /// to -1 (Windows default recording device). Same logic as <c>NAudioRecorder</c>.
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

        return -1;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Persist to WAV for the Whisper analysis.
        try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); }
        catch (Exception ex) { _logger.LogDebug(ex, "reading capture write error"); }

        // Stream a COPY of the PCM bytes to listeners (the NAudio buffer is reused).
        var handler = FrameAvailable;
        if (handler is null || e.BytesRecorded == 0) return;
        var copy = new byte[e.BytesRecorded];
        System.Buffer.BlockCopy(e.Buffer, 0, copy, 0, e.BytesRecorded);
        try { handler(this, new ReadingPcmFrame(copy, e.BytesRecorded)); }
        catch (Exception ex) { _logger.LogDebug(ex, "FrameAvailable handler threw"); }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _stoppedSignal?.Set();
        if (e.Exception is not null)
            _logger.LogWarning(e.Exception, "Reading capture stopped with exception");
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
            catch (Exception ex) { _logger.LogDebug(ex, "disposing reading WaveInEvent"); }
            _waveIn = null;
        }
        if (_writer is not null)
        {
            try { _writer.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "disposing reading WaveFileWriter"); }
            _writer = null;
        }
    }

    public void Dispose() => Stop();
}
