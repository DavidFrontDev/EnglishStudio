using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Writing;
using EnglishStudio.App.Views.Writing;
using EnglishStudio.Modules.Ielts.Speaking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace EnglishStudio.App.ViewModels.Speaking;

public partial class SpeakingSessionViewModel : ObservableObject
{
    private readonly ISpeakingTestService _svc;
    private readonly ISpeakingFeedbackService _feedback;
    private readonly IServiceProvider _services;
    private readonly ILogger<SpeakingSessionViewModel> _log;

    private int _attemptId;
    private SpeakingMode _mode;

    // FullMock-only — staged content for transitioning Part1→2→3.
    private FullMockBundle? _fullMock;
    private SpeakingTopicSummary? _activeTopic;

    private readonly List<SpeakingResponseRecord> _allResponses = new();
    private AiProcessingWindow? _aiWindow;
    private AiProcessingViewModel? _aiVm;

    [ObservableProperty] private object? _currentPartViewModel;
    [ObservableProperty] private string _sessionTitle = "IELTS Speaking";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>Fired after all responses are saved & attempt finished. Caller opens result.</summary>
    public event Action<int>? Submitted;
    /// <summary>Fired when user cancels — caller is expected to delete the attempt.</summary>
    public event Action<int?>? Cancelled;

    public SpeakingSessionViewModel(
        ISpeakingTestService svc,
        ISpeakingFeedbackService feedback,
        IServiceProvider services,
        ILogger<SpeakingSessionViewModel> log)
    {
        _svc = svc;
        _feedback = feedback;
        _services = services;
        _log = log;
    }

    public async Task StartAsync(SpeakingMode mode, int? part1BankId, int? part2BankId)
    {
        _mode = mode;

        IsBusy = true;
        StatusText = Loc.Tr("Speaking_PreparingTopic");
        try
        {
            switch (mode)
            {
                case SpeakingMode.FullMock:
                    {
                        // Привязка к Cambridge-тесту, если задан Part2-банк (mock-экзамен); иначе случайный набор.
                        _fullMock = part2BankId is int p2Bank
                            ? await _svc.StartFullMockAsync(p2Bank)
                            : await _svc.StartFullMockAsync();
                        _activeTopic = _fullMock.Part2Topic;
                        _attemptId = await _svc.StartAttemptAsync(mode, _fullMock.Part2Topic.BankId);
                        SetTitle(_fullMock.Part2Topic.TopicLabel);
                        BeginPart1(_fullMock.Part1Questions);
                        break;
                    }
                case SpeakingMode.Part1Only:
                    {
                        IReadOnlyList<SpeakingQuestionDetail> questions;
                        SpeakingTopicSummary? topic = null;
                        if (part1BankId is int bid)
                        {
                            topic = (await _svc.ListTopicsAsync(SpeakingPart.Part1))
                                .FirstOrDefault(t => t.BankId == bid);
                            questions = await _svc.GetQuestionsForBankAsync(bid);
                        }
                        else
                        {
                            // Fallback: random topic
                            topic = await _svc.PickRandomTopicAsync(SpeakingPart.Part1);
                            questions = topic is null ? Array.Empty<SpeakingQuestionDetail>()
                                : await _svc.GetQuestionsForBankAsync(topic.BankId);
                        }
                        _activeTopic = topic;
                        _attemptId = await _svc.StartAttemptAsync(mode, topic?.BankId);
                        SetTitle(topic?.TopicLabel);
                        BeginPart1(questions);
                        break;
                    }
                case SpeakingMode.Part2Only:
                    {
                        var topic = await ResolveTopicAsync(SpeakingPart.Part2, part2BankId);
                        var questions = topic is null
                            ? Array.Empty<SpeakingQuestionDetail>()
                            : await _svc.GetQuestionsForBankAsync(topic.BankId);
                        _activeTopic = topic;
                        _attemptId = await _svc.StartAttemptAsync(mode, topic?.BankId);
                        SetTitle(topic?.TopicLabel);
                        if (topic is not null && questions.Count > 0)
                            BeginPart2(topic, questions[0]);
                        break;
                    }
                case SpeakingMode.Part3Only:
                    {
                        // For Part3Only the picker selects a Part 2 topic; we follow its link.
                        var part2Topic = await ResolveTopicAsync(SpeakingPart.Part2, part2BankId);
                        var followUps = await GetPart3FollowUpsAsync(part2Topic);
                        _activeTopic = part2Topic;
                        _attemptId = await _svc.StartAttemptAsync(mode, part2Topic?.BankId);
                        SetTitle(part2Topic?.TopicLabel);
                        BeginPart3(followUps);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start Speaking session");
            StatusText = Loc.Tr("Speaking_FailedToStartSession") + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<SpeakingTopicSummary?> ResolveTopicAsync(SpeakingPart part, int? bankId)
    {
        if (bankId is int id)
        {
            var topics = await _svc.ListTopicsAsync(part);
            var t = topics.FirstOrDefault(x => x.BankId == id);
            if (t is not null) return t;
        }
        return await _svc.PickRandomTopicAsync(part);
    }

    private async Task<IReadOnlyList<SpeakingQuestionDetail>> GetPart3FollowUpsAsync(SpeakingTopicSummary? part2Topic)
    {
        if (part2Topic is null) return Array.Empty<SpeakingQuestionDetail>();

        // Look for a Part 3 bank linked to this Part 2 bank. The contract doesn't expose
        // bank links directly, so we iterate Part 3 banks and pick one whose first follow-up
        // question has FollowUpToQuestionId pointing at any question in the Part 2 bank.
        var part2Questions = await _svc.GetQuestionsForBankAsync(part2Topic.BankId);
        var part2QIds = part2Questions.Select(q => q.QuestionId).ToHashSet();

        var part3Topics = await _svc.ListTopicsAsync(SpeakingPart.Part3);
        foreach (var t in part3Topics)
        {
            var qs = await _svc.GetQuestionsForBankAsync(t.BankId);
            if (qs.Any(q => q.FollowUpToQuestionId is int fid && part2QIds.Contains(fid)))
                return qs;
        }

        // Fallback: random Part 3 bank
        var fallback = await _svc.PickRandomTopicAsync(SpeakingPart.Part3);
        return fallback is null ? Array.Empty<SpeakingQuestionDetail>()
            : await _svc.GetQuestionsForBankAsync(fallback.BankId);
    }

    private void SetTitle(string? topicLabel)
    {
        SessionTitle = string.IsNullOrWhiteSpace(topicLabel)
            ? "IELTS Speaking"
            : $"IELTS Speaking — {topicLabel}";
    }

    private void BeginPart1(IReadOnlyList<SpeakingQuestionDetail> questions)
    {
        var vm = _services.GetRequiredService<SpeakingPart1ViewModel>();
        vm.Initialize(_attemptId, questions);
        vm.Completed += OnPart1Completed;
        CurrentPartViewModel = vm;
        ProgressText = _mode == SpeakingMode.FullMock ? Loc.Tr("Speaking_Step1of3Part1") : "Part 1";
    }

    private void BeginPart2(SpeakingTopicSummary topic, SpeakingQuestionDetail question)
    {
        var vm = _services.GetRequiredService<SpeakingPart2ViewModel>();
        vm.Initialize(_attemptId, topic, question, subpoints: topic.CueCardSubpoints);
        vm.Completed += OnPart2Completed;
        CurrentPartViewModel = vm;
        ProgressText = _mode == SpeakingMode.FullMock ? Loc.Tr("Speaking_Step2of3Part2") : "Part 2";
    }

    private void BeginPart3(IReadOnlyList<SpeakingQuestionDetail> questions)
    {
        var vm = _services.GetRequiredService<SpeakingPart3ViewModel>();
        vm.Initialize(_attemptId, questions);
        vm.Completed += OnPart3Completed;
        CurrentPartViewModel = vm;
        ProgressText = _mode == SpeakingMode.FullMock ? Loc.Tr("Speaking_Step3of3Part3") : "Part 3";
    }

    private async void OnPart1Completed(IReadOnlyList<SpeakingResponseRecord> responses)
    {
        if (CurrentPartViewModel is SpeakingPart1ViewModel old) old.Completed -= OnPart1Completed;
        _allResponses.AddRange(responses);
        if (_mode == SpeakingMode.FullMock && _fullMock is not null)
        {
            BeginPart2(_fullMock.Part2Topic, _fullMock.Part2Question);
        }
        else
        {
            await FinishAttemptAsync();
        }
    }

    private async void OnPart2Completed(SpeakingResponseRecord response)
    {
        if (CurrentPartViewModel is SpeakingPart2ViewModel old) old.Completed -= OnPart2Completed;
        _allResponses.Add(response);
        if (_mode == SpeakingMode.FullMock && _fullMock is not null)
        {
            BeginPart3(_fullMock.Part3FollowUps);
        }
        else
        {
            await FinishAttemptAsync();
        }
    }

    private async void OnPart3Completed(IReadOnlyList<SpeakingResponseRecord> responses)
    {
        if (CurrentPartViewModel is SpeakingPart3ViewModel old) old.Completed -= OnPart3Completed;
        _allResponses.AddRange(responses);
        await FinishAttemptAsync();
    }

    private async Task FinishAttemptAsync()
    {
        IsBusy = true;
        StatusText = Loc.Tr("Speaking_SavingResponses");
        try
        {
            foreach (var r in _allResponses)
            {
                await _svc.SaveResponseAsync(
                    _attemptId,
                    r.QuestionId,
                    r.AudioPath,
                    r.Transcript,
                    r.DurationSeconds,
                    r.Words);
            }
            await _svc.FinishAttemptAsync(_attemptId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to persist responses / finish attempt");
            StatusText = Loc.Tr("Speaking_FailedToSaveResponses") + ex.Message;
            IsBusy = false;
            return;
        }

        _aiVm = new AiProcessingViewModel { StatusText = Loc.Tr("Speaking_AiEvaluating") };
        _aiVm.Start();
        _aiWindow = new AiProcessingWindow
        {
            DataContext = _aiVm,
            Owner = Application.Current.MainWindow
        };
        _aiWindow.Show();

        try
        {
            await _feedback.EvaluateAndSaveAsync(_attemptId);
            _aiVm.StatusText = Loc.Tr("Speaking_AiDoneOpeningResult");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Speaking evaluation failed");
            _aiVm.StatusText = Loc.Tr("Speaking_AiEvaluationFailed") + ex.Message;
            await Task.Delay(1500);
        }
        finally
        {
            _aiVm?.Stop();
            _aiWindow?.Close();
            _aiWindow = null;
            _aiVm = null;
            IsBusy = false;
        }

        Submitted?.Invoke(_attemptId);
    }

    [RelayCommand]
    private void Cancel()
    {
        Cleanup();
        Cancelled?.Invoke(_attemptId != 0 ? _attemptId : null);
    }

    private bool _isCleanedUp;

    /// <summary>Stops timers/recording of the active part. Safe to call more than once (Cancel + window Closed).</summary>
    public void Cleanup()
    {
        if (_isCleanedUp) return;
        _isCleanedUp = true;
        switch (CurrentPartViewModel)
        {
            case SpeakingPart1ViewModel p1: p1.ForceStop(); break;
            case SpeakingPart2ViewModel p2: p2.ForceStop(); break;
            case SpeakingPart3ViewModel p3: p3.ForceStop(); break;
        }
    }
}
