using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Reading.Audio;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Shadowing trainer (F4): step through the text's sentences — listen to TTS, repeat into the
/// mic, get a per-word score with phoneme hints (F3). Built fresh per open (transient).
/// </summary>
public partial class ShadowingViewModel : ObservableObject
{
    private readonly IShadowingService _shadowing;
    private readonly ITtsService _tts;
    private readonly IReadingAudioCapture _capture;
    private readonly IPhonemeFeedbackService _phonemes;
    private readonly ILogger<ShadowingViewModel> _log;

    private IReadOnlyList<TextToken> _tokens = Array.Empty<TextToken>();
    private readonly Dictionary<int, string> _wordByIndex = new();
    private List<ShadowingSentence> _sentences = new();

    [ObservableProperty] private bool _hasSentences;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private string _currentText = string.Empty;
    [ObservableProperty] private string _positionText = string.Empty;
    [ObservableProperty] private bool _ttsAvailable;
    [ObservableProperty] private bool _micAvailable;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _isSpeaking;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isScoring;
    [ObservableProperty] private double _rate = 1.0;
    [ObservableProperty] private string? _selectedVoice;

    [ObservableProperty] private bool _hasScore;
    [ObservableProperty] private double _scoreAccuracy;
    [ObservableProperty] private int _wordsExpected;
    [ObservableProperty] private int _wordsSpoken;

    public IReadOnlyList<string> Voices { get; private set; } = Array.Empty<string>();

    public ObservableCollection<ShadowingWordViewModel> WordResults { get; } = new();

    public bool CanGoPrev => CurrentIndex > 0;
    public bool CanGoNext => CurrentIndex < _sentences.Count - 1;

    /// <summary>Raised when the user dismisses the trainer.</summary>
    public event Action? CloseRequested;

    public ShadowingViewModel(
        IShadowingService shadowing,
        ITtsService tts,
        IReadingAudioCapture capture,
        IPhonemeFeedbackService phonemes,
        ILogger<ShadowingViewModel> log)
    {
        _shadowing = shadowing;
        _tts = tts;
        _capture = capture;
        _phonemes = phonemes;
        _log = log;
    }

    public Task InitializeAsync(IReadOnlyList<TextToken> tokens, CancellationToken ct = default)
    {
        _tokens = tokens;
        _wordByIndex.Clear();
        foreach (var t in tokens)
            if (t.Kind == TokenKind.Word && t.WordIndex is int wi)
                _wordByIndex[wi] = t.Text;

        try { _sentences = _shadowing.GetSentences(tokens).ToList(); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to split text into sentences");
            _sentences = new();
        }

        HasSentences = _sentences.Count > 0;
        TtsAvailable = _tts.IsAvailable;
        Voices = _tts.Voices;
        SelectedVoice = Voices.FirstOrDefault();

        try { MicAvailable = _capture.IsMicrophoneAvailable(); }
        catch { MicAvailable = false; }

        if (!HasSentences)
            StatusText = Loc.Tr("ReadStudy_ShadowNoSentences");
        else if (!TtsAvailable)
            StatusText = Loc.Tr("ReadStudy_ShadowNoTts");
        else
            StatusText = null;

        SetCurrent(0);
        return Task.CompletedTask;
    }

    private void SetCurrent(int index)
    {
        if (_sentences.Count == 0)
        {
            CurrentText = string.Empty;
            PositionText = string.Empty;
            return;
        }

        CurrentIndex = Math.Clamp(index, 0, _sentences.Count - 1);
        CurrentText = _sentences[CurrentIndex].Text;
        PositionText = Loc.Format("ReadStudy_ShadowSentenceOf", CurrentIndex + 1, _sentences.Count);

        HasScore = false;
        WordResults.Clear();

        OnPropertyChanged(nameof(CanGoPrev));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private async Task PlayCurrent()
    {
        if (!TtsAvailable || string.IsNullOrWhiteSpace(CurrentText)) return;
        IsSpeaking = true;
        try { await _tts.SpeakAsync(CurrentText, SelectedVoice, Rate); }
        catch (Exception ex) { _log.LogWarning(ex, "TTS playback failed"); }
        finally { IsSpeaking = false; }
    }

    [RelayCommand]
    private async Task StopSpeak()
    {
        try { await _tts.StopAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "TTS stop failed"); }
        IsSpeaking = false;
    }

    [RelayCommand]
    private async Task RecordToggle()
    {
        if (IsScoring) return;

        if (IsRecording)
        {
            string? wav;
            try { wav = _capture.Stop(); }
            catch (Exception ex) { _log.LogWarning(ex, "Capture stop failed"); wav = null; }
            IsRecording = false;
            await ScoreAsync(wav);
            return;
        }

        if (!MicAvailable)
        {
            StatusText = Loc.Tr("ReadStudy_ShadowNoMic");
            return;
        }

        HasScore = false;
        WordResults.Clear();
        try
        {
            _capture.Start();
            IsRecording = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Capture start failed");
            StatusText = Loc.Tr("ReadStudy_ShadowRecordFailed");
        }
    }

    private async Task ScoreAsync(string? wavPath)
    {
        var index = CurrentIndex;
        if (index < 0 || index >= _sentences.Count) return;

        IsScoring = true;
        try
        {
            var score = await _shadowing.ScoreRepeatAsync(wavPath ?? string.Empty, _sentences[index], _tokens);
            if (CurrentIndex != index) return;
            if (score is null)
            {
                StatusText = Loc.Tr("ReadStudy_ShadowScoreFailed");
                return;
            }

            ScoreAccuracy = score.AccuracyPct;
            WordsExpected = score.WordsExpected;
            WordsSpoken = score.WordsSpoken;

            WordResults.Clear();
            foreach (var outcome in score.Words)
            {
                var text = _wordByIndex.TryGetValue(outcome.TokenIndex, out var w) ? w : "?";
                WordResults.Add(new ShadowingWordViewModel(text, outcome, _phonemes, _log));
            }
            HasScore = true;
            StatusText = null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shadowing scoring failed");
            if (CurrentIndex == index) StatusText = Loc.Tr("ReadStudy_ShadowScoreFailed");
        }
        finally
        {
            IsScoring = false;
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        await StopSpeak();
        DiscardRecording();
        SetCurrent(CurrentIndex + 1);
    }

    [RelayCommand]
    private async Task Prev()
    {
        await StopSpeak();
        DiscardRecording();
        SetCurrent(CurrentIndex - 1);
    }

    private void DiscardRecording()
    {
        if (!IsRecording) return;
        try { _capture.Stop(); } catch { }
        IsRecording = false;
    }

    [RelayCommand]
    private void Close()
    {
        Cleanup();
        CloseRequested?.Invoke();
    }

    /// <summary>Stops TTS / recording and releases the capture device.</summary>
    public void Cleanup()
    {
        try { _ = _tts.StopAsync(); } catch { }
        if (IsRecording)
        {
            try { _capture.Stop(); } catch { }
            IsRecording = false;
        }
        try { _capture.Dispose(); } catch { }
    }
}

/// <summary>One word in a shadowing score: outcome colour + an expandable phoneme guide.</summary>
public partial class ShadowingWordViewModel : ObservableObject
{
    private readonly IPhonemeFeedbackService _phonemes;
    private readonly ILogger _log;

    [ObservableProperty] private bool _isGuideVisible;
    [ObservableProperty] private PhonemeGuideViewModel? _guide;

    public string Word { get; }
    public bool IsSkipped { get; }
    public bool IsMispronounced { get; }
    public bool IsGood { get; }
    public double? Score { get; }

    public ShadowingWordViewModel(string word, ReadingWordOutcome outcome, IPhonemeFeedbackService phonemes, ILogger log)
    {
        Word = word;
        _phonemes = phonemes;
        _log = log;
        IsSkipped = outcome.Skipped;
        IsMispronounced = outcome.Mispronounced;
        IsGood = outcome is { Spoken: true, Skipped: false, Mispronounced: false };
        Score = outcome.Score;
    }

    [RelayCommand]
    private void ToggleGuide()
    {
        if (Guide is null)
        {
            try
            {
                var guide = _phonemes.BuildGuide(ReadingTokenizer.NormalizeWord(Word));
                Guide = new PhonemeGuideViewModel(guide);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to build phoneme guide for '{Word}'", Word);
                return;
            }
        }
        IsGuideVisible = !IsGuideVisible;
    }
}
