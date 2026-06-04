using System.IO;
using System.Threading;
using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.Audio;

public sealed class MicrophoneTester : IMicrophoneTester
{
    private readonly ILogger<MicrophoneTester> _logger;
    private readonly object _gate = new();

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _path;
    private SynchronizationContext? _syncContext;

    public MicrophoneTester(ILogger<MicrophoneTester> logger) => _logger = logger;

    public bool IsActive { get; private set; }

    public event EventHandler<double>? LevelChanged;

    public IReadOnlyList<MicrophoneDeviceInfo> GetDevices()
    {
        var list = new List<MicrophoneDeviceInfo>
        {
            new(-1, "Системное устройство по умолчанию")
        };

        try
        {
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                string name;
                try { name = WaveInEvent.GetCapabilities(i).ProductName; }
                catch { name = $"Микрофон {i}"; }
                list.Add(new MicrophoneDeviceInfo(i, name));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate input devices");
        }

        return list;
    }

    public void Start(int deviceNumber, bool record)
    {
        lock (_gate)
        {
            StopInternal();
            // Captured on the UI thread so LevelChanged is raised back on the UI thread.
            _syncContext = SynchronizationContext.Current;

            // Same format as the real recorder so the playback reflects what Whisper will receive.
            var fmt = new WaveFormat(16000, 16, 1);
            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = fmt,
                    DeviceNumber = deviceNumber,
                    BufferMilliseconds = 50 // snappy VU meter (~20 updates/sec)
                };

                if (record)
                {
                    var dir = Path.Combine(DictionaryPaths.AppDataRoot, "MicTest");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, "mic_test.wav");
                    _writer = new WaveFileWriter(_path, fmt);
                }

                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsActive = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start microphone test on device {Device}", deviceNumber);
                StopInternal();
            }
        }
    }

    public string? Stop()
    {
        lock (_gate)
        {
            if (!IsActive) return _path;

            try { _waveIn?.StopRecording(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Error stopping microphone test"); }

            try { _writer?.Flush(); }
            catch (Exception ex) { _logger.LogDebug(ex, "writer flush"); }

            var path = _writer is not null ? _path : null;
            StopInternal();
            IsActive = false;
            RaiseLevel(0);
            return path;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);

            var max = 0;
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                var abs = sample == short.MinValue ? short.MaxValue : Math.Abs(sample);
                if (abs > max) max = abs;
            }

            RaiseLevel(max / 32768.0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DataAvailable level error");
        }
    }

    private void RaiseLevel(double level)
    {
        var handler = LevelChanged;
        if (handler is null) return;

        if (_syncContext is not null)
            _syncContext.Post(_ => handler(this, level), null);
        else
            handler(this, level);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            _logger.LogWarning(e.Exception, "Microphone test stopped with exception");
    }

    private void StopInternal()
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
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopInternal();
            IsActive = false;
        }
    }
}
