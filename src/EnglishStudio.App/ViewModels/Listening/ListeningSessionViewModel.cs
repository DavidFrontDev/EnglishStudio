using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Reading.Questions;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using EnglishStudio.Modules.Ielts.Listening;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Listening;

public partial class ListeningSessionViewModel : ObservableObject, IDisposable
{
    private const int ExamDurationSeconds = 40 * 60;  // 30 min audio + 10 min transfer
    private const int AutoSaveDebounceMs = 400;

    private readonly IListeningTestService _testSvc;
    private readonly ITestRunner _runner;
    private readonly IListeningAudioPlayer _audio;
    private readonly ILogger<ListeningSessionViewModel> _log;

    private TestAttempt? _attempt;
    private DispatcherTimer? _timer;
    private readonly Dictionary<int, DispatcherTimer> _saveDebounce = new();
    private readonly Dictionary<int, string?> _audioByPartIndex = new();
    private readonly Dictionary<int, string?> _transcriptByPartIndex = new();
    private int _loadedAudioPartIndex = -1;
    private bool _isFinishing;
    private bool _updatingProgressFromPlayer;

    [ObservableProperty] private string _testTitle = string.Empty;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isTrainingMode;

    public ObservableCollection<ListeningCardViewModel> Cards { get; } = new();
    private IReadOnlyList<IReadingQuestionViewModel> _allQuestions = Array.Empty<IReadingQuestionViewModel>();

    [ObservableProperty] private int _currentCardIndex;
    [ObservableProperty] private ListeningCardViewModel? _currentCard;

    [ObservableProperty] private int _answeredCount;
    [ObservableProperty] private int _totalQuestions;
    public string ProgressLabel => Loc.Format("Listening_ProgressLabel", AnsweredCount, TotalQuestions);
    public string CardPositionLabel => Cards.Count == 0 ? string.Empty : Loc.Format("Listening_CardPositionLabel", CurrentCardIndex + 1, Cards.Count);

    // ── Timer ──
    [ObservableProperty] private bool _timerVisible;
    [ObservableProperty] private int _secondsRemaining;
    [ObservableProperty] private string _timerLabel = "40:00";

    // ── Audio bar ──
    [ObservableProperty] private bool _audioAvailable;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _canSeek;
    [ObservableProperty] private string _audioTimeLabel = "00:00 / 00:00";
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private double _volume = 1.0;

    // ── Transcript (training only) ──
    [ObservableProperty] private bool _transcriptAvailable;
    [ObservableProperty] private bool _showTranscript;
    [ObservableProperty] private string _currentTranscript = string.Empty;

    public event Action<int>? Finished;
    public event Action? Cancelled;

    public ListeningSessionViewModel(
        IListeningTestService testSvc,
        ITestRunner runner,
        IListeningAudioPlayer audio,
        ILogger<ListeningSessionViewModel> log)
    {
        _testSvc = testSvc;
        _runner = runner;
        _audio = audio;
        _log = log;
        _audio.PositionChanged += OnAudioPositionChanged;
        _audio.PlaybackEnded += OnAudioEnded;
    }

    public async Task StartAsync(int testSetId, bool trainingMode)
    {
        IsTrainingMode = trainingMode;
        CanSeek = trainingMode;
        IsLoading = true;

        var test = await _testSvc.GetFullAsync(testSetId);
        if (test is null) { IsLoading = false; return; }

        TestTitle = test.Title;
        Cards.Clear();
        _audioByPartIndex.Clear();

        var allQuestions = new List<IReadingQuestionViewModel>();
        var partIndex = 0;
        var cardNumber = 1;
        foreach (var part in test.Parts.OrderBy(p => p.OrderInTest))
        {
            _audioByPartIndex[partIndex] = part.AudioPath;
            _transcriptByPartIndex[partIndex] = part.Transcript;
            foreach (var group in part.Groups.OrderBy(g => g.OrderInPart))
            {
                var card = new ListeningCardViewModel(group, partIndex, part.OrderInTest, part.Title)
                {
                    CardNumber = cardNumber++
                };
                Cards.Add(card);
                allQuestions.AddRange(card.Questions);
            }
            partIndex++;
        }

        _allQuestions = allQuestions;
        TotalQuestions = allQuestions.Count;
        foreach (var q in allQuestions)
        {
            q.PropertyChanged += (_, _) =>
            {
                RecomputeAnsweredCount();
                ScheduleSave(q);
            };
        }

        _attempt = await _runner.StartAsync(testSetId, trainingMode);

        CurrentCardIndex = 0;
        CurrentCard = Cards.FirstOrDefault();
        UpdateCurrentFlags();
        SyncAudioForCurrentCard(autoPlayIfExam: false);
        UpdateTranscript();

        TimerVisible = !trainingMode;
        if (TimerVisible)
        {
            SecondsRemaining = ExamDurationSeconds;
            UpdateTimerLabel();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        IsLoading = false;
        OnPropertyChanged(nameof(CardPositionLabel));
    }

    partial void OnCurrentCardIndexChanged(int value)
    {
        if (value >= 0 && value < Cards.Count) CurrentCard = Cards[value];
        UpdateCurrentFlags();
        PrevCardCommand.NotifyCanExecuteChanged();
        NextCardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CardPositionLabel));
        SyncAudioForCurrentCard(autoPlayIfExam: !IsTrainingMode);
        UpdateTranscript();
    }

    [RelayCommand]
    private void ToggleTranscript()
    {
        if (TranscriptAvailable) ShowTranscript = !ShowTranscript;
    }

    private void UpdateTranscript()
    {
        var tr = CurrentCard is not null && _transcriptByPartIndex.TryGetValue(CurrentCard.PartIndex, out var t) ? t : null;
        CurrentTranscript = tr ?? string.Empty;
        TranscriptAvailable = IsTrainingMode && !string.IsNullOrWhiteSpace(tr);
        if (!TranscriptAvailable) ShowTranscript = false;
    }

    private void UpdateCurrentFlags()
    {
        for (var i = 0; i < Cards.Count; i++) Cards[i].IsCurrent = i == CurrentCardIndex;
    }

    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void PrevCard() => CurrentCardIndex--;
    private bool CanGoPrev() => CurrentCardIndex > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextCard() => CurrentCardIndex++;
    private bool CanGoNext() => CurrentCardIndex < Cards.Count - 1;

    [RelayCommand]
    private void JumpToCard(ListeningCardViewModel? card)
    {
        if (card is null) return;
        var idx = Cards.IndexOf(card);
        if (idx >= 0) CurrentCardIndex = idx;
    }

    // ── Audio ──
    private void SyncAudioForCurrentCard(bool autoPlayIfExam)
    {
        if (CurrentCard is null) return;
        var partIdx = CurrentCard.PartIndex;
        if (partIdx == _loadedAudioPartIndex) return;  // same part audio already loaded

        _audioByPartIndex.TryGetValue(partIdx, out var path);
        _loadedAudioPartIndex = partIdx;

        if (string.IsNullOrWhiteSpace(path))
        {
            _audio.Stop();
            AudioAvailable = false;
            UpdateAudioLabels();
            return;
        }

        _audio.Load(path);
        AudioAvailable = true;
        if (autoPlayIfExam) _audio.Play();
        UpdateAudioLabels();
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (!AudioAvailable) return;
        if (_audio.IsPlaying) _audio.Pause();
        else _audio.Play();
    }

    [RelayCommand]
    private void Replay()
    {
        if (!AudioAvailable) return;
        _audio.Seek(TimeSpan.Zero);
        _audio.Play();
    }

    /// <summary>Called from the slider when the user seeks (training only).</summary>
    public void SeekToFraction(double fraction)
    {
        if (!CanSeek || !AudioAvailable) return;
        var dur = _audio.Duration;
        _audio.Seek(TimeSpan.FromSeconds(dur.TotalSeconds * Math.Clamp(fraction, 0, 1)));
    }

    private void OnAudioPositionChanged()
    {
        IsPlaying = _audio.IsPlaying;
        UpdateAudioLabels();
    }

    private void OnAudioEnded() => IsPlaying = false;

    private void UpdateAudioLabels()
    {
        var pos = _audio.Position;
        var dur = _audio.Duration;
        AudioTimeLabel = $"{Fmt(pos)} / {Fmt(dur)}";
        _updatingProgressFromPlayer = true;
        ProgressFraction = dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0;
        _updatingProgressFromPlayer = false;
    }

    partial void OnProgressFractionChanged(double value)
    {
        if (_updatingProgressFromPlayer) return;
        SeekToFraction(value);
    }

    partial void OnVolumeChanged(double value) => _audio.Volume = (float)value;

    private static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    // ── Answers / persistence ──
    private void RecomputeAnsweredCount()
    {
        AnsweredCount = _allQuestions.Count(q => q.HasAnswer);
        OnPropertyChanged(nameof(ProgressLabel));
    }

    private void ScheduleSave(IReadingQuestionViewModel q)
    {
        if (_attempt is null) return;
        if (!_saveDebounce.TryGetValue(q.QuestionId, out var t))
        {
            t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoSaveDebounceMs) };
            t.Tick += async (_, _) =>
            {
                t.Stop();
                try { await _runner.SubmitAnswerAsync(_attempt!.Id, q.QuestionId, q.GetAnswerJson()); }
                catch (Exception ex) { _log.LogWarning(ex, "Listening auto-save failed for question {Q}", q.QuestionId); }
            };
            _saveDebounce[q.QuestionId] = t;
        }
        t.Stop();
        t.Start();
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (_attempt is null || _isFinishing) return;
        _isFinishing = true;
        var completed = false;
        try
        {
            StopTimer();
            _audio.Stop();
            foreach (var t in _saveDebounce.Values) t.Stop();
            _saveDebounce.Clear();

            foreach (var q in _allQuestions)
                await _runner.SubmitAnswerAsync(_attempt.Id, q.QuestionId, q.GetAnswerJson());

            await _runner.FinishAsync(_attempt.Id);
            Finished?.Invoke(_attempt.Id);
            completed = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to finish listening attempt {Id}", _attempt.Id);
        }
        finally
        {
            if (!completed) _isFinishing = false;
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        StopTimer();
        _audio.Stop();
        foreach (var t in _saveDebounce.Values) t.Stop();
        _saveDebounce.Clear();
        if (_attempt is not null)
        {
            try { await _runner.AbandonAsync(_attempt.Id); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to abandon listening attempt {Id}", _attempt.Id); }
        }
        Cancelled?.Invoke();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        SecondsRemaining--;
        UpdateTimerLabel();
        if (SecondsRemaining <= 0)
        {
            StopTimer();
            _ = FinishAsync();
        }
    }

    private void StopTimer()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }
    }

    private void UpdateTimerLabel()
    {
        var c = Math.Max(0, SecondsRemaining);
        TimerLabel = $"{c / 60:00}:{c % 60:00}";
    }

    public void Dispose()
    {
        StopTimer();
        _audio.PositionChanged -= OnAudioPositionChanged;
        _audio.PlaybackEnded -= OnAudioEnded;
        _audio.Dispose();
        foreach (var t in _saveDebounce.Values) t.Stop();
        _saveDebounce.Clear();
    }
}
