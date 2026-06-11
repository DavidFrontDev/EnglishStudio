using System.Diagnostics;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Reading.Audio;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.Reading;

/// <summary>
/// Live read-along controller: microphone capture → Vosk streaming ASR → forced-alignment
/// cursor over the reference text. Raises <see cref="IReadAlongController"/> events on the UI
/// thread (captured <see cref="SynchronizationContext"/> in <see cref="StartAsync"/>), so the
/// reader UI never marshals. Registered transient — one instance per reading session.
/// </summary>
public sealed class VoskReadAlongController : IReadAlongController, IDisposable
{
    // Vosk partials refine their last token, so we only feed a partial word once it is no
    // longer the trailing (still-refining) one; the final word of an utterance is fed at Final.
    private readonly VoskSpeechRecognizer _recognizer;
    private readonly IReadingAudioCapture _capture;
    private readonly ILogger<VoskReadAlongController> _logger;

    private readonly object _finishGate = new();
    private readonly object _sessionGate = new();
    private SynchronizationContext? _uiContext;
    private ReadAlongAligner? _aligner;
    private VoskStreamSession? _session;
    private Stopwatch? _stopwatch;
    private TimeSpan _lastProgressReport;
    private int _consumedPartialWords;
    private bool _starting;
    private bool _finished;
    private ReadingRunResult? _result;

    public VoskReadAlongController(
        VoskSpeechRecognizer recognizer,
        IReadingAudioCapture capture,
        ILogger<VoskReadAlongController> logger)
    {
        _recognizer = recognizer;
        _capture = capture;
        _logger = logger;
    }

    public ReadAlongState State { get; private set; } = ReadAlongState.Idle;
    public bool IsModelReady => _recognizer.IsModelReady;

    public event EventHandler<ReadAlongState>? StateChanged;
    public event EventHandler<string>? ModelDownloadStatus;
    public event EventHandler<ReadAlongProgress>? Progress;
    public event EventHandler<ReadingRunResult>? Finished;

    public async Task<bool> EnsureModelAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        _uiContext ??= SynchronizationContext.Current;
        if (_recognizer.IsModelReady) return true;

        SetState(ReadAlongState.LoadingModel);
        var relay = new Progress<string>(msg =>
        {
            progress?.Report(msg);
            RaiseModelStatus(msg);
        });

        var ok = await _recognizer.EnsureModelAsync(relay, ct);
        if (!ok) SetState(ReadAlongState.Error);
        else if (State == ReadAlongState.LoadingModel) SetState(ReadAlongState.Idle);
        return ok;
    }

    public async Task StartAsync(IReadOnlyList<TextToken> referenceTokens, CancellationToken ct = default)
    {
        // Capture the UI context up front (before any await) so all events marshal back to it.
        _uiContext = SynchronizationContext.Current;

        if (State == ReadAlongState.Listening || _starting) return;
        _starting = true;
        try
        {
            if (!_capture.IsMicrophoneAvailable())
            {
                _logger.LogWarning("No microphone — cannot start read-along.");
                RaiseModelStatus(Loc.Tr("Vosk_MicNotFound"));
                SetState(ReadAlongState.Error);
                return;
            }

            var ok = await EnsureModelAsync(null, ct);
            if (!ok)
            {
                SetState(ReadAlongState.Error);
                return;
            }

            var referenceWords = referenceTokens
                .Where(t => t.Kind == TokenKind.Word && t.WordIndex.HasValue)
                .OrderBy(t => t.WordIndex!.Value)
                .Select(t => ReadingTokenizer.NormalizeWord(t.Text))
                .ToList();

            _aligner = new ReadAlongAligner(referenceWords);
            _consumedPartialWords = 0;
            _finished = false;
            _result = null;
            _lastProgressReport = TimeSpan.Zero;

            try
            {
                _session = _recognizer.CreateSession();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create Vosk session.");
                SetState(ReadAlongState.Error);
                return;
            }

            _stopwatch = Stopwatch.StartNew();
            _capture.FrameAvailable += OnFrame;
            _capture.Start();
            SetState(ReadAlongState.Listening);

            // Empty text — nothing to read; finish immediately.
            if (_aligner.Total == 0)
                _ = Task.Run(() => FinishCore(completedByEnd: true));
        }
        finally
        {
            _starting = false;
        }
    }

    public Task<ReadingRunResult> StopAsync() => Task.Run(() => FinishCore(completedByEnd: false));

    private void OnFrame(object? sender, ReadingPcmFrame frame)
    {
        var aligner = _aligner;
        if (aligner is null) return;

        try
        {
            lock (_sessionGate)
            {
                var session = _session;
                if (session is null || _finished) return;

                var chunk = session.Accept(frame.Buffer, frame.Count);
                if (chunk.IsFinal)
                {
                    FeedRange(aligner, chunk.Words, _consumedPartialWords, chunk.Words.Count);
                    _consumedPartialWords = 0;
                }
                else
                {
                    // Feed all but the trailing (still-refining) partial word.
                    var stable = chunk.Words.Count - 1;
                    if (stable > _consumedPartialWords)
                    {
                        FeedRange(aligner, chunk.Words, _consumedPartialWords, stable);
                        _consumedPartialWords = stable;
                    }
                }
            }

            ReportProgress(aligner, force: false);

            if (aligner.IsComplete && aligner.Total > 0)
                _ = Task.Run(() => FinishCore(completedByEnd: true));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing audio frame.");
        }
    }

    private static void FeedRange(ReadAlongAligner aligner, IReadOnlyList<string> words, int start, int end)
    {
        if (end <= start) return;
        var slice = new List<string>(end - start);
        for (var i = start; i < end && i < words.Count; i++)
            slice.Add(ReadingTokenizer.NormalizeWord(words[i]));
        aligner.Accept(slice);
    }

    private void ReportProgress(ReadAlongAligner aligner, bool force)
    {
        var elapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        // Throttle to ~5 updates/sec.
        if (!force && elapsed - _lastProgressReport < TimeSpan.FromMilliseconds(200)) return;
        _lastProgressReport = elapsed;

        var words = aligner.Cursor;
        var elapsedSec = elapsed.TotalSeconds;
        var wpm = elapsedSec > 1.0 ? words / (elapsedSec / 60.0) : 0;
        var progress = new ReadAlongProgress(
            aligner.Cursor, words, Math.Round(wpm, 1), Math.Round(elapsedSec, 1));
        Post(() => Progress?.Invoke(this, progress));
    }

    private ReadingRunResult FinishCore(bool completedByEnd)
    {
        lock (_finishGate)
        {
            if (_finished) return _result!;
            _finished = true;

            _capture.FrameAvailable -= OnFrame;
            string? wav;
            try { wav = _capture.Stop(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error stopping capture."); wav = _capture.LastWavPath; }

            var aligner = _aligner;
            lock (_sessionGate)
            {
                try
                {
                    if (_session is not null)
                    {
                        var chunk = _session.Flush();
                        if (aligner is not null)
                            FeedRange(aligner, chunk.Words, _consumedPartialWords, chunk.Words.Count);
                        _consumedPartialWords = 0;
                    }
                }
                catch (Exception ex) { _logger.LogDebug(ex, "Vosk flush failed."); }

                try { _session?.Dispose(); } catch { /* best effort */ }
                _session = null;
            }

            if (aligner is not null) ReportProgress(aligner, force: true);

            _stopwatch?.Stop();
            var elapsedSec = _stopwatch?.Elapsed.TotalSeconds ?? 0;
            var words = aligner?.Cursor ?? 0;
            var total = aligner?.Total ?? 0;
            var wpm = elapsedSec > 1.0 ? words / (elapsedSec / 60.0) : 0;
            var completed = completedByEnd || (total > 0 && words >= total);

            _result = new ReadingRunResult(
                words, Math.Round(wpm, 1), Math.Round(elapsedSec, 1), completed, wav);

            SetState(ReadAlongState.Finished);
            var result = _result;
            Post(() => Finished?.Invoke(this, result));
            return result;
        }
    }

    private void SetState(ReadAlongState state)
    {
        if (State == state) return;
        State = state;
        Post(() => StateChanged?.Invoke(this, state));
    }

    private void RaiseModelStatus(string message) => Post(() => ModelDownloadStatus?.Invoke(this, message));

    private void Post(Action action)
    {
        if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    public void Dispose()
    {
        try { if (!_finished) FinishCore(completedByEnd: false); } catch { /* best effort */ }
        _capture.FrameAvailable -= OnFrame;
        try { _capture.Dispose(); } catch { /* best effort */ }
        lock (_sessionGate)
        {
            try { _session?.Dispose(); } catch { /* best effort */ }
            _session = null;
        }
    }
}
