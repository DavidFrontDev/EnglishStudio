using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Ielts.Speaking;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Speaking;

/// <summary>Cue-card long-turn: 60s prep then 120s response, both visualised.</summary>
public partial class SpeakingPart2ViewModel : ObservableObject
{
    public const int PrepSeconds = 60;
    public const int RecordSeconds = 120;
    public SpeakingPart Part => SpeakingPart.Part2;

    private readonly IAudioRecorder _recorder;
    private readonly IWhisperTranscriber _whisper;
    private readonly ILogger<SpeakingPart2ViewModel> _log;

    private DispatcherTimer? _prepTimer;
    private DispatcherTimer? _recordTimer;
    private DateTime _recordStartedAt;
    private int _attemptId;
    private SpeakingQuestionDetail? _question;
    private string? _currentWavPath;

    [ObservableProperty] private string _cueCardPrompt = string.Empty;
    [ObservableProperty] private string _topicLabel = string.Empty;
    [ObservableProperty] private string _subpointsText = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;

    [ObservableProperty] private int _prepRemaining = PrepSeconds;
    [ObservableProperty] private int _recordRemaining = RecordSeconds;

    [ObservableProperty] private bool _isPreparing;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private double _transcribeProgress;   // 0..1 — для индикатора процента
    [ObservableProperty] private bool _isFinished;

    [ObservableProperty] private string? _transcript;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _partTitle = "Part 2 — Long turn";

    private SpeakingResponseRecord? _capturedResponse;
    public SpeakingResponseRecord? CapturedResponse => _capturedResponse;

    public string PrepDisplay => FormatMmSs(PrepRemaining);
    public string RecordDisplay => FormatMmSs(RecordRemaining);

    public bool CanStartPrep => !IsPreparing && !IsRecording && !IsFinished;
    public bool CanStartRecording => !IsPreparing && !IsRecording && !IsTranscribing && !IsFinished;
    public bool CanSkipPrep => IsPreparing;
    public bool CanStopRecord => IsRecording;
    public bool CanFinish => IsFinished && !IsTranscribing;

    public event Action<SpeakingResponseRecord>? Completed;

    public SpeakingPart2ViewModel(
        IAudioRecorder recorder,
        IWhisperTranscriber whisper,
        ILogger<SpeakingPart2ViewModel> log)
    {
        _recorder = recorder;
        _whisper = whisper;
        _log = log;
    }

    public void Initialize(
        int attemptId,
        SpeakingTopicSummary topic,
        SpeakingQuestionDetail question,
        IReadOnlyList<string>? subpoints = null)
    {
        _attemptId = attemptId;
        _question = question;
        CueCardPrompt = topic.CueCardPrompt ?? question.Text;
        TopicLabel = topic.TopicLabel;
        SubpointsText = subpoints is { Count: > 0 }
            ? "• " + string.Join("\n• ", subpoints)
            : string.Empty;
        Notes = string.Empty;
        PrepRemaining = PrepSeconds;
        RecordRemaining = RecordSeconds;
        IsPreparing = false;
        IsRecording = false;
        IsTranscribing = false;
        IsFinished = false;
        Transcript = null;
        _capturedResponse = null;
        RaiseAll();
    }

    [RelayCommand]
    private void StartPrep()
    {
        if (!CanStartPrep) return;
        IsPreparing = true;
        PrepRemaining = PrepSeconds;
        _prepTimer?.Stop();
        _prepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _prepTimer.Tick += (_, _) =>
        {
            PrepRemaining--;
            OnPropertyChanged(nameof(PrepDisplay));
            if (PrepRemaining <= 0)
            {
                _prepTimer?.Stop();
                IsPreparing = false;
                _ = StartRecordAsync();
            }
        };
        _prepTimer.Start();
        RaiseAll();
    }

    [RelayCommand]
    private void SkipPrep()
    {
        if (!CanSkipPrep) return;
        _prepTimer?.Stop();
        IsPreparing = false;
        _ = StartRecordAsync();
    }

    [RelayCommand]
    private async Task StartRecordAsync()
    {
        if (IsRecording) return;
        try
        {
            _currentWavPath = BuildWavPath();
            _recorder.StartRecording();
            if (!_recorder.IsRecording)
            {
                StatusText = Loc.Tr("Speaking_MicAccessDenied");
                return;
            }
            IsRecording = true;
            RecordRemaining = RecordSeconds;
            _recordStartedAt = DateTime.UtcNow;
            _recordTimer?.Stop();
            _recordTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _recordTimer.Tick += (_, _) =>
            {
                RecordRemaining = Math.Max(0, RecordSeconds - (int)(DateTime.UtcNow - _recordStartedAt).TotalSeconds);
                OnPropertyChanged(nameof(RecordDisplay));
                if (RecordRemaining <= 0) _ = StopRecordAsync();
            };
            _recordTimer.Start();
            RaiseAll();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Part 2 recording");
            StatusText = Loc.Tr("Speaking_RecordingError") + ex.Message;
        }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StopRecordAsync()
    {
        if (!IsRecording) return;
        _recordTimer?.Stop();
        var path = _recorder.StopRecording();
        IsRecording = false;
        var duration = (int)(DateTime.UtcNow - _recordStartedAt).TotalSeconds;
        if (duration <= 0) duration = 1;
        if (path is null)
        {
            StatusText = Loc.Tr("Speaking_RecordingFailed");
            RaiseAll();
            return;
        }
        var finalPath = _currentWavPath ?? path;
        try
        {
            if (!string.Equals(path, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                File.Copy(path, finalPath, overwrite: true);
                try { File.Delete(path); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not relocate Part 2 WAV.");
            finalPath = path;
        }

        IsTranscribing = true;
        TranscribeProgress = 0;
        StatusText = Loc.Tr("Speaking_Transcribing");
        RaiseAll();
        IReadOnlyList<SpokenWord>? words = null;
        var progress = new Progress<double>(p => TranscribeProgress = p);
        try
        {
            var result = await _whisper.TranscribeWithTimestampsAsync(finalPath, progress);
            Transcript = result?.Text ?? string.Empty;
            words = result is null ? null : MapToSpokenWords(result.Words);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Transcription failed");
            Transcript = string.Empty;
            StatusText = Loc.Tr("Speaking_TranscriptFailed");
        }
        finally
        {
            IsTranscribing = false;
        }

        if (_question is not null)
        {
            _capturedResponse = new SpeakingResponseRecord(
                Part,
                _question.QuestionId,
                _question.Text,
                finalPath,
                Transcript,
                duration,
                words);
        }
        IsFinished = true;
        RaiseAll();
    }

    private static IReadOnlyList<SpokenWord> MapToSpokenWords(IReadOnlyList<WordTimestamp> words)
    {
        if (words.Count == 0) return Array.Empty<SpokenWord>();
        var result = new SpokenWord[words.Count];
        for (var i = 0; i < words.Count; i++)
            result[i] = new SpokenWord(words[i].Word, words[i].StartSec, words[i].EndSec);
        return result;
    }

    [RelayCommand]
    private void Finish()
    {
        if (!CanFinish || _capturedResponse is null) return;
        Completed?.Invoke(_capturedResponse);
    }

    public void ForceStop()
    {
        try { _recordTimer?.Stop(); _prepTimer?.Stop(); } catch { /* ignore */ }
        if (_recorder.IsRecording) _recorder.StopRecording();
    }

    private string BuildWavPath()
    {
        var root = Path.Combine(DictionaryPaths.AppDataRoot, "Speaking", "Audio", _attemptId.ToString());
        Directory.CreateDirectory(root);
        return Path.Combine(root, "part2.wav");
    }

    private static string FormatMmSs(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CanStartPrep));
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanSkipPrep));
        OnPropertyChanged(nameof(CanStopRecord));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(PrepDisplay));
        OnPropertyChanged(nameof(RecordDisplay));
    }
}
