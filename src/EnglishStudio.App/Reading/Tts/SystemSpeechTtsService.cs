using System.Speech.Synthesis;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.Reading.Tts;

/// <summary>
/// Text-to-speech for shadowing (F4) on <see cref="SpeechSynthesizer"/> (System.Speech / SAPI —
/// in-box, no TFM change, voices = installed SAPI voices). English voices only; degrades to
/// <see cref="IsAvailable"/>=false when none are installed.
/// </summary>
public sealed class SystemSpeechTtsService : ITtsService, IDisposable
{
    private readonly ILogger<SystemSpeechTtsService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initLock = new();

    private SpeechSynthesizer? _synth;
    private List<string> _voices = new();
    private bool _initialized;

    public SystemSpeechTtsService(ILogger<SystemSpeechTtsService> log) => _log = log;

    public bool IsAvailable { get { EnsureInit(); return _synth is not null && _voices.Count > 0; } }

    public IReadOnlyList<string> Voices { get { EnsureInit(); return _voices; } }

    public async Task SpeakAsync(string text, string? voice = null, double rate = 1.0, CancellationToken ct = default)
    {
        EnsureInit();
        if (_synth is null || string.IsNullOrWhiteSpace(text)) return;
        ct.ThrowIfCancellationRequested();

        await _gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(voice) && _voices.Contains(voice))
            {
                try { _synth.SelectVoice(voice); }
                catch (Exception ex) { _log.LogDebug(ex, "Could not select voice '{Voice}'", voice); }
            }
            _synth.Rate = MapRate(rate);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? s, SpeakCompletedEventArgs e) => tcs.TrySetResult();
            _synth.SpeakCompleted += Completed;
            using var reg = ct.Register(() => { try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ } });
            try
            {
                _synth.SpeakAsync(text);
                await tcs.Task;
            }
            finally
            {
                _synth.SpeakCompleted -= Completed;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task StopAsync()
    {
        try { _synth?.SpeakAsyncCancelAll(); }
        catch (Exception ex) { _log.LogDebug(ex, "TTS stop failed"); }
        return Task.CompletedTask;
    }

    private void EnsureInit()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            try
            {
                var synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                _voices = synth.GetInstalledVoices()
                    .Where(v => v.Enabled
                                && string.Equals(v.VoiceInfo.Culture?.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
                    .Select(v => v.VoiceInfo.Name)
                    .ToList();
                _synth = synth;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "TTS initialisation failed; shadowing playback disabled.");
                _synth = null;
                _voices = new List<string>();
            }
            _initialized = true;
        }
    }

    /// <summary>Maps a 0.75–1.0(+) playback rate to System.Speech's −10..10 scale (1.0 → 0).</summary>
    private static int MapRate(double rate) => Math.Clamp((int)Math.Round((rate - 1.0) * 20.0), -10, 10);

    public void Dispose()
    {
        try { _synth?.Dispose(); } catch { /* ignore */ }
        _gate.Dispose();
    }
}
