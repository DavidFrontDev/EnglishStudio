using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Listening;
using EnglishStudio.App.ViewModels.Reading.Questions;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Core.Scoring;
using EnglishStudio.Modules.Ielts.Reading;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Reading;

/// <summary>
/// Reading test session — same card-carousel UX as Listening (one card at a time on the right,
/// numbered card indicator, prev/next nav), but the left zone shows the passage text for the
/// current card's part instead of an audio player. Cards are <see cref="ListeningCardViewModel"/>
/// so both modules share the exact same card rendering stack.
/// </summary>
public partial class ReadingTestViewModel : ObservableObject, IDisposable
{
    private const int ExamDurationSeconds = 60 * 60;   // 60 minutes for Academic Reading
    private const int AutoSaveDebounceMs = 400;

    private readonly IReadingTestService _testSvc;
    private readonly ITestRunner _runner;
    private readonly ILogger<ReadingTestViewModel> _log;

    private TestAttempt? _attempt;
    private DispatcherTimer? _timer;
    private readonly Dictionary<int, DispatcherTimer> _saveDebounce = new();
    private readonly Dictionary<int, PassageInfo> _passageByPartIndex = new();
    private bool _isFinishing;

    [ObservableProperty] private string _testTitle = string.Empty;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isTrainingMode;

    public ObservableCollection<ListeningCardViewModel> Cards { get; } = new();
    private IReadOnlyList<IReadingQuestionViewModel> _allQuestions = Array.Empty<IReadingQuestionViewModel>();

    [ObservableProperty] private int _currentCardIndex;
    [ObservableProperty] private ListeningCardViewModel? _currentCard;

    // ── Left passage zone ──
    [ObservableProperty] private string _passageTitle = string.Empty;
    [ObservableProperty] private string _passageBody = string.Empty;
    [ObservableProperty] private string _passageNote = string.Empty;

    [ObservableProperty] private int _answeredCount;
    [ObservableProperty] private int _totalQuestions;
    public string ProgressLabel => Loc.Format("ReadIelts_ProgressLabel", AnsweredCount, TotalQuestions);
    public string CardPositionLabel => Cards.Count == 0 ? string.Empty : Loc.Format("ReadIelts_CardPositionLabel", CurrentCardIndex + 1, Cards.Count);

    // ── Timer ──
    [ObservableProperty] private bool _timerVisible;
    [ObservableProperty] private int _secondsRemaining;
    [ObservableProperty] private string _timerLabel = "60:00";

    public event Action<int>? Finished;
    public event Action? Cancelled;

    public ReadingTestViewModel(
        IReadingTestService testSvc,
        ITestRunner runner,
        ILogger<ReadingTestViewModel> log)
    {
        _testSvc = testSvc;
        _runner = runner;
        _log = log;
    }

    public async Task StartAsync(int testSetId, bool trainingMode)
    {
        IsTrainingMode = trainingMode;
        IsLoading = true;

        var test = await _testSvc.GetFullAsync(testSetId);
        if (test is null) { IsLoading = false; return; }

        TestTitle = test.Title;
        Cards.Clear();
        _passageByPartIndex.Clear();

        var allQuestions = new List<IReadingQuestionViewModel>();
        var partIndex = 0;
        var cardNumber = 1;
        foreach (var part in test.Parts.OrderBy(p => p.OrderInTest))
        {
            _passageByPartIndex[partIndex] = new PassageInfo(part.Title, part.BodyText ?? string.Empty, part.IntroNoteRu ?? string.Empty);
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
        UpdatePassage();

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
        UpdatePassage();
    }

    private void UpdatePassage()
    {
        if (CurrentCard is null) return;
        if (_passageByPartIndex.TryGetValue(CurrentCard.PartIndex, out var p))
        {
            PassageTitle = p.Title;
            PassageBody = p.Body;
            PassageNote = p.Note;
        }
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
                catch (Exception ex) { _log.LogWarning(ex, "Reading auto-save failed for question {Q}", q.QuestionId); }
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
            _log.LogError(ex, "Failed to finish reading attempt {Id}", _attempt.Id);
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
        foreach (var t in _saveDebounce.Values) t.Stop();
        _saveDebounce.Clear();
        if (_attempt is not null)
        {
            try { await _runner.AbandonAsync(_attempt.Id); }
            catch (Exception ex) { _log.LogWarning(ex, "Failed to abandon reading attempt {Id}", _attempt.Id); }
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
        foreach (var t in _saveDebounce.Values) t.Stop();
        _saveDebounce.Clear();
    }

    private readonly record struct PassageInfo(string Title, string Body, string Note);
}
