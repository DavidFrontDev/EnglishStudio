using System.Collections.ObjectModel;
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

/// <summary>4–5 follow-up questions with 60s recording each. No prep phase.</summary>
public partial class SpeakingPart3ViewModel : ObservableObject
{
    public const int RecordSeconds = 60;
    public SpeakingPart Part => SpeakingPart.Part3;

    private readonly IAudioRecorder _recorder;
    private readonly IWhisperTranscriber _whisper;
    private readonly ILogger<SpeakingPart3ViewModel> _log;

    private DispatcherTimer? _recordTimer;
    private DateTime _recordStartedAt;
    private int _attemptId;
    private string? _currentWavPath;

    public ObservableCollection<SpeakingQuestionDetail> Questions { get; } = new();
    private readonly List<SpeakingResponseRecord> _responses = new();
    public IReadOnlyList<SpeakingResponseRecord> Responses => _responses;

    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private SpeakingQuestionDetail? _currentQuestion;
    [ObservableProperty] private int _recordRemaining = RecordSeconds;
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isTranscribing;
    [ObservableProperty] private double _transcribeProgress;   // 0..1 — для индикатора процента
    [ObservableProperty] private string? _currentTranscript;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _partTitle = "Part 3 — Discussion";

    public string ProgressLabel =>
        Questions.Count == 0
            ? "Part 3"
            : $"Part 3 — Question {Math.Min(CurrentIndex + 1, Questions.Count)} of {Questions.Count}";

    public bool CanRecord => !IsRecording && !IsTranscribing && CurrentTranscript is null;
    public bool CanGoNext => !IsRecording && !IsTranscribing && CurrentTranscript is not null;

    public event Action<IReadOnlyList<SpeakingResponseRecord>>? Completed;

    public SpeakingPart3ViewModel(
        IAudioRecorder recorder,
        IWhisperTranscriber whisper,
        ILogger<SpeakingPart3ViewModel> log)
    {
        _recorder = recorder;
        _whisper = whisper;
        _log = log;
    }

    public void Initialize(int attemptId, IReadOnlyList<SpeakingQuestionDetail> questions)
    {
        _attemptId = attemptId;
        Questions.Clear();
        foreach (var q in questions) Questions.Add(q);
        _responses.Clear();
        CurrentIndex = 0;
        AdvanceToCurrent();
    }

    private void AdvanceToCurrent()
    {
        CurrentQuestion = CurrentIndex < Questions.Count ? Questions[CurrentIndex] : null;
        CurrentTranscript = null;
        RecordRemaining = RecordSeconds;
        IsRecording = false;
        IsTranscribing = false;
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private async Task StartRecordAsync()
    {
        if (!CanRecord || CurrentQuestion is null) return;
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
                if (RecordRemaining <= 0) _ = StopRecordAsync();
            };
            _recordTimer.Start();
            OnPropertyChanged(nameof(CanRecord));
            OnPropertyChanged(nameof(CanGoNext));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Part 3 recording");
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
            OnPropertyChanged(nameof(CanRecord));
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
            _log.LogWarning(ex, "Could not relocate Part 3 WAV.");
            finalPath = path;
        }

        IsTranscribing = true;
        TranscribeProgress = 0;
        StatusText = Loc.Tr("Speaking_Transcribing");
        OnPropertyChanged(nameof(CanGoNext));
        IReadOnlyList<SpokenWord>? words = null;
        var progress = new Progress<double>(p => TranscribeProgress = p);
        try
        {
            var result = await _whisper.TranscribeWithTimestampsAsync(finalPath, progress);
            CurrentTranscript = result?.Text ?? string.Empty;
            words = result is null ? null : MapToSpokenWords(result.Words);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Transcription failed");
            CurrentTranscript = string.Empty;
            StatusText = Loc.Tr("Speaking_TranscriptFailed");
        }
        finally
        {
            IsTranscribing = false;
        }

        if (CurrentQuestion is not null)
        {
            _responses.Add(new SpeakingResponseRecord(
                Part,
                CurrentQuestion.QuestionId,
                CurrentQuestion.Text,
                finalPath,
                CurrentTranscript,
                duration,
                words));
        }

        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanGoNext));
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
    private void GoNext()
    {
        if (!CanGoNext) return;
        if (CurrentIndex >= Questions.Count - 1)
        {
            Completed?.Invoke(_responses);
            return;
        }
        CurrentIndex++;
        AdvanceToCurrent();
    }

    public void ForceStop()
    {
        try { _recordTimer?.Stop(); } catch { /* ignore */ }
        if (_recorder.IsRecording) _recorder.StopRecording();
    }

    private string BuildWavPath()
    {
        var root = Path.Combine(DictionaryPaths.AppDataRoot, "Speaking", "Audio", _attemptId.ToString());
        Directory.CreateDirectory(root);
        var order = _responses.Count + 1;
        return Path.Combine(root, $"part3_{order:D2}.wav");
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnIsTranscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRecord));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnCurrentIndexChanged(int value) =>
        OnPropertyChanged(nameof(ProgressLabel));
}
