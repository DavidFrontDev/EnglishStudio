using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Speaking;
using Microsoft.Extensions.Logging;
using NAudio.Wave;

namespace EnglishStudio.App.ViewModels.Speaking;

public partial class SpeakingResultViewModel : ObservableObject
{
    private readonly ISpeakingTestService _svc;
    private readonly ISpeakingFeedbackService _feedback;
    private readonly ILogger<SpeakingResultViewModel> _log;

    [ObservableProperty] private SpeakingMode _mode;
    [ObservableProperty] private double? _bandFluencyCoherence;
    [ObservableProperty] private double? _bandLexicalResource;
    [ObservableProperty] private double? _bandGrammar;
    [ObservableProperty] private double? _bandPronunciation;
    [ObservableProperty] private double? _bandOverall;
    [ObservableProperty] private string _feedbackRu = string.Empty;
    [ObservableProperty] private string _feedbackEn = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _attemptId;

    /// <summary>Examiner comment in the current UI language (falls back to the other if empty).</summary>
    [ObservableProperty] private string _feedbackText = string.Empty;

    public ObservableCollection<string> Strengths { get; } = new();
    public ObservableCollection<string> Improvements { get; } = new();
    public ObservableCollection<SpeakingResponseRow> Responses { get; } = new();

    public bool HasBands => BandOverall.HasValue;

    [ObservableProperty] private double _avgWpm;
    [ObservableProperty] private double _avgPauseRatio;
    [ObservableProperty] private int _totalFillers;
    [ObservableProperty] private double _avgTypeTokenRatio;

    public string WpmColor =>
        AvgWpm <= 0 ? "#888"
        : AvgWpm < 100 ? "#E45F5F"
        : AvgWpm > 180 ? "#E8A24D"
        : "#5BC080";

    public string PauseRatioColor =>
        AvgPauseRatio <= 0 ? "#888"
        : AvgPauseRatio > 0.25 ? "#E8A24D"
        : "#5BC080";

    public string ModeLabel => Mode switch
    {
        SpeakingMode.FullMock => "Full mock",
        SpeakingMode.Part1Only => "Part 1",
        SpeakingMode.Part2Only => "Part 2",
        SpeakingMode.Part3Only => "Part 3",
        _ => string.Empty
    };

    public SpeakingResultViewModel(
        ISpeakingTestService svc,
        ISpeakingFeedbackService feedback,
        ILogger<SpeakingResultViewModel> log)
    {
        _svc = svc;
        _feedback = feedback;
        _log = log;
    }

    /// <summary>Останавливает воспроизведение всех ответов (вызывается при уходе с экрана).</summary>
    public void StopAllPlayback()
    {
        foreach (var r in Responses) r.StopPlayback();
    }

    private void OnRowPlaybackStarted(SpeakingResponseRow started)
    {
        // Только один ответ играет одновременно — глушим остальные.
        foreach (var r in Responses)
            if (!ReferenceEquals(r, started)) r.StopPlayback();
    }

    public async Task LoadAsync(int attemptId)
    {
        AttemptId = attemptId;
        IsBusy = true;
        StatusText = string.Empty;
        try
        {
            var attempt = await _svc.GetAttemptAsync(attemptId);
            if (attempt is null)
            {
                StatusText = Loc.Tr("Speaking_AttemptNotFound");
                return;
            }

            Mode = attempt.Summary.Mode;
            BandFluencyCoherence = attempt.BandFluencyCoherence;
            BandLexicalResource = attempt.BandLexicalResource;
            BandGrammar = attempt.BandGrammar;
            BandPronunciation = attempt.BandPronunciation;
            BandOverall = attempt.Summary.BandOverall;

            Strengths.Clear();
            Improvements.Clear();
            FeedbackRu = string.Empty;
            FeedbackEn = string.Empty;
            FeedbackText = string.Empty;

            if (!string.IsNullOrWhiteSpace(attempt.FeedbackJson))
            {
                try
                {
                    var report = JsonSerializer.Deserialize<SpeakingScoreReport>(attempt.FeedbackJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (report is not null)
                    {
                        FeedbackRu = report.FeedbackRu ?? string.Empty;
                        FeedbackEn = report.FeedbackEn ?? string.Empty;
                        FeedbackText = Loc.Pick(FeedbackEn, FeedbackRu);
                        foreach (var s in report.Strengths) Strengths.Add(s);
                        foreach (var imp in report.Improvements) Improvements.Add(imp);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Cannot parse Speaking feedback JSON");
                }
            }

            foreach (var old in Responses)
            {
                old.PlaybackStarted -= OnRowPlaybackStarted;
                old.Dispose();
            }
            Responses.Clear();
            int order = 1;
            foreach (var r in attempt.Responses)
            {
                var row = new SpeakingResponseRow(r, order++);
                row.PlaybackStarted += OnRowPlaybackStarted;
                Responses.Add(row);
            }
            UpdateMetricAverages(attempt.Responses);

            OnPropertyChanged(nameof(HasBands));
            OnPropertyChanged(nameof(ModeLabel));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load Speaking result");
            StatusText = Loc.Tr("Speaking_FailedToLoadResult");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateMetricAverages(IReadOnlyList<SpeakingResponseDetail> responses)
    {
        var wpms = responses.Where(r => r.WpmRate.HasValue).Select(r => r.WpmRate!.Value).ToList();
        var pauses = responses.Where(r => r.PauseRatio.HasValue).Select(r => r.PauseRatio!.Value).ToList();
        var ttrs = responses.Where(r => r.TypeTokenRatio.HasValue).Select(r => r.TypeTokenRatio!.Value).ToList();
        AvgWpm = wpms.Count == 0 ? 0 : wpms.Average();
        AvgPauseRatio = pauses.Count == 0 ? 0 : pauses.Average();
        TotalFillers = responses.Sum(r => r.FillerCount ?? 0);
        AvgTypeTokenRatio = ttrs.Count == 0 ? 0 : ttrs.Average();
        OnPropertyChanged(nameof(WpmColor));
        OnPropertyChanged(nameof(PauseRatioColor));
    }

    [RelayCommand]
    private async Task ReevaluateAsync()
    {
        if (AttemptId == 0) return;
        IsBusy = true;
        StatusText = Loc.Tr("Speaking_ReevaluatingAi");
        try
        {
            await _feedback.EvaluateAndSaveAsync(AttemptId);
            await LoadAsync(AttemptId);
            StatusText = Loc.Tr("Speaking_EvaluationUpdated");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Reevaluation failed");
            StatusText = Loc.Tr("Speaking_ReevaluationFailed") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Один ответ Speaking с самодостаточным плеером (NAudio): play/pause/resume/stop + перемотка
/// слайдером. Позиция обновляется <see cref="DispatcherTimer"/>'ом; <see cref="PlaybackStarted"/>
/// поднимается при старте, чтобы хост заглушил остальные ответы (играет только один).
/// </summary>
public partial class SpeakingResponseRow : ObservableObject, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private DispatcherTimer? _timer;
    private bool _suppressSeek;   // защита от эха таймера в сеттере PositionSeconds
    private bool _isScrubbing;    // пользователь тащит ползунок

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _positionSeconds;
    [ObservableProperty] private double _durationSeconds;

    public int OrderInAttempt { get; }
    public string QuestionText { get; }
    public string AudioPath { get; }
    public string? Transcript { get; }
    public int DurationSeconds_Recorded { get; }
    public double? WpmRate { get; }
    public double? PauseRatio { get; }
    public int? FillerCount { get; }
    public double? TypeTokenRatio { get; }

    public bool HasAudio => !string.IsNullOrWhiteSpace(AudioPath) && File.Exists(AudioPath);

    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";
    public string PositionLabel => Format(PositionSeconds);
    public string TotalLabel => Format(DurationSeconds);

    /// <summary>Поднимается при старте воспроизведения — хост глушит другие строки.</summary>
    public event Action<SpeakingResponseRow>? PlaybackStarted;

    public SpeakingResponseRow(SpeakingResponseDetail detail, int orderInAttempt)
    {
        OrderInAttempt = orderInAttempt;
        QuestionText = detail.QuestionText;
        AudioPath = detail.AudioPath;
        Transcript = detail.Transcript;
        DurationSeconds_Recorded = detail.DurationSeconds;
        WpmRate = detail.WpmRate;
        PauseRatio = detail.PauseRatio;
        FillerCount = detail.FillerCount;
        TypeTokenRatio = detail.TypeTokenRatio;
        DurationSeconds = detail.DurationSeconds; // ориентир для слайдера до загрузки файла
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayPauseGlyph));
    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(TotalLabel));

    partial void OnPositionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(PositionLabel));
        if (_suppressSeek) return;   // изменение пришло от таймера — это не перемотка
        SeekTo(value);               // изменение от слайдера (пользователь)
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasAudio) return;

        if (_output is null)
        {
            StartFromCurrent();
            return;
        }

        if (IsPlaying)
        {
            _output.Pause();
            IsPlaying = false;
            _timer?.Stop();
        }
        else
        {
            _output.Play();
            IsPlaying = true;
            EnsureTimer();
            _timer!.Start();
        }
    }

    [RelayCommand]
    private void Stop() => StopInternal();

    /// <summary>Публичная остановка (хост глушит другие ответы / уход с экрана).</summary>
    public void StopPlayback() => StopInternal();

    /// <summary>Слайдер: начало перетаскивания — приостановить обновления позиции таймером.</summary>
    public void BeginScrub() => _isScrubbing = true;

    /// <summary>Слайдер: конец перетаскивания — финальная перемотка.</summary>
    public void EndScrub()
    {
        _isScrubbing = false;
        SeekTo(PositionSeconds);
    }

    private void StartFromCurrent()
    {
        try
        {
            _reader = new AudioFileReader(AudioPath);
            DurationSeconds = _reader.TotalTime.TotalSeconds;
            if (PositionSeconds > 0 && PositionSeconds < DurationSeconds)
                _reader.CurrentTime = TimeSpan.FromSeconds(PositionSeconds);

            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            IsPlaying = true;
            EnsureTimer();
            _timer!.Start();
            PlaybackStarted?.Invoke(this);
        }
        catch
        {
            StopInternal();
        }
    }

    private void SeekTo(double seconds)
    {
        if (_reader is null) return;
        try
        {
            var max = _reader.TotalTime.TotalSeconds;
            _reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 0, max));
        }
        catch { /* ignore */ }
    }

    private void EnsureTimer()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) =>
        {
            if (_reader is null || _isScrubbing) return;
            SetPositionSuppressed(_reader.CurrentTime.TotalSeconds);
        };
    }

    private void SetPositionSuppressed(double value)
    {
        _suppressSeek = true;
        PositionSeconds = value;
        _suppressSeek = false;
    }

    private void StopInternal()
    {
        _timer?.Stop();
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;   // снимаем до Stop(), чтобы не словить событие
            try { _output.Stop(); _output.Dispose(); } catch { /* ignore */ }
            _output = null;
        }
        if (_reader is not null)
        {
            try { _reader.Dispose(); } catch { /* ignore */ }
            _reader = null;
        }
        IsPlaying = false;
        SetPositionSuppressed(0);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Событие приходит из потока воспроизведения — маршалим в UI и сбрасываем в начало.
        _dispatcher.Invoke(StopInternal);
    }

    private static string Format(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
        var total = (int)Math.Round(seconds);
        return $"{total / 60:D2}:{total % 60:D2}";
    }

    public void Dispose() => StopInternal();
}
