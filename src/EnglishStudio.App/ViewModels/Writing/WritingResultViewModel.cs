using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Writing;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Writing;

public partial class WritingResultViewModel : ObservableObject
{
    private readonly IWritingTaskService _taskSvc;
    private readonly WritingFeedbackService _feedback;
    private readonly ILogger<WritingResultViewModel> _log;

    [ObservableProperty] private WritingAttemptResult? _task1;
    [ObservableProperty] private WritingAttemptResult? _task2;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _testTitle = string.Empty;

    public double? CombinedBand
    {
        get
        {
            if (Task1?.BandOverall is null && Task2?.BandOverall is null) return null;
            // IELTS Writing weighting: Task 1 = 1/3, Task 2 = 2/3
            var t1 = Task1?.BandOverall ?? Task2?.BandOverall ?? 0;
            var t2 = Task2?.BandOverall ?? Task1?.BandOverall ?? 0;
            var raw = (t1 + 2 * t2) / 3.0;
            // Snap to nearest .5
            return Math.Round(raw * 2) / 2;
        }
    }
    public bool HasCombinedBand => CombinedBand.HasValue;

    public WritingResultViewModel(
        IWritingTaskService taskSvc,
        WritingFeedbackService feedback,
        ILogger<WritingResultViewModel> log)
    {
        _taskSvc = taskSvc;
        _feedback = feedback;
        _log = log;
    }

    public async Task LoadAsync(IReadOnlyList<int> attemptIds)
    {
        IsBusy = true;
        try
        {
            Task1 = null;
            Task2 = null;
            foreach (var id in attemptIds)
            {
                var att = await _taskSvc.GetAttemptAsync(id);
                if (att?.WritingTask is null) continue;

                var pane = BuildPane(att);
                if (att.WritingTask.OrderInSet == 2 || att.WritingTask.Kind == WritingTaskKind.Task2)
                    Task2 = pane;
                else
                    Task1 = pane;
            }

            TestTitle = (Task1?.TaskCode ?? Task2?.TaskCode ?? string.Empty).Split('-')[..^1] is { } parts && parts.Length > 0
                ? string.Join('-', parts)
                : string.Empty;

            OnPropertyChanged(nameof(CombinedBand));
            OnPropertyChanged(nameof(HasCombinedBand));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load writing result");
            StatusText = Loc.Tr("Writing_LoadResultFailed");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ReevaluateAllAsync()
    {
        IsBusy = true;
        StatusText = Loc.Tr("Writing_ReevaluatingBothTasks");
        try
        {
            var ids = new List<int>();
            if (Task1 != null) ids.Add(Task1.AttemptId);
            if (Task2 != null) ids.Add(Task2.AttemptId);

            foreach (var id in ids)
            {
                await _feedback.EvaluateAndSaveAsync(id);
            }
            await LoadAsync(ids);
            StatusText = Loc.Tr("Writing_EvalUpdated");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to reevaluate writing attempts");
            StatusText = Loc.Tr("Writing_ReevalFailed");
        }
        finally { IsBusy = false; }
    }

    private static WritingAttemptResult BuildPane(WritingAttempt attempt)
    {
        var report = TryParseReport(attempt.FeedbackJson);
        var modelAnswer = attempt.WritingTask.ModelAnswers.OrderBy(m => m.BandLevel).LastOrDefault();

        return new WritingAttemptResult(
            attempt.Id,
            attempt.WritingTask.Code,
            attempt.WritingTask.Kind,
            attempt.WritingTask.PromptText,
            attempt.WritingTask.ImagePath,
            attempt.UserText,
            attempt.WordCount,
            attempt.DurationSeconds,
            attempt.BandTaskAchievement,
            attempt.BandCoherence,
            attempt.BandLexical,
            attempt.BandGrammar,
            attempt.BandOverall,
            report?.FeedbackEn ?? string.Empty,
            report?.FeedbackRu ?? string.Empty,
            new ObservableCollection<EssayIssueRow>(
                (report?.Issues ?? new List<EssayIssue>())
                    .Select(i => new EssayIssueRow(i.Category, i.Quote, Loc.Pick(i.ExplanationEn, i.ExplanationRu), i.Suggestion))),
            modelAnswer?.AnswerText,
            modelAnswer?.BandLevel ?? 0);
    }

    private static EssayScoreReport? TryParseReport(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<EssayScoreReport>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

public partial class WritingAttemptResult : ObservableObject
{
    [ObservableProperty] private bool _showModelAnswer;

    public int AttemptId { get; }
    public string TaskCode { get; }
    public WritingTaskKind Kind { get; }
    public string PromptText { get; }
    public string? ImagePath { get; }
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
    public string UserText { get; }
    public int WordCount { get; }
    public int DurationSeconds { get; }
    public double? BandTaskAchievement { get; }
    public double? BandCoherence { get; }
    public double? BandLexical { get; }
    public double? BandGrammar { get; }
    public double? BandOverall { get; }
    public bool HasBands => BandOverall.HasValue;
    public bool HasNoBands => !HasBands;
    public string FeedbackEn { get; }
    public string FeedbackRu { get; }

    /// <summary>The examiner comment in the current UI language (falls back to the other if empty).</summary>
    public string Feedback => Loc.Pick(FeedbackEn, FeedbackRu);

    public ObservableCollection<EssayIssueRow> Issues { get; }
    public string? ModelAnswerText { get; }
    public int ModelAnswerBand { get; }
    public bool HasModelAnswer => !string.IsNullOrWhiteSpace(ModelAnswerText);

    public string KindLabel => Kind switch
    {
        WritingTaskKind.Task1Academic => "Task 1",
        WritingTaskKind.Task1GeneralTraining => "Task 1 GT",
        WritingTaskKind.Task2 => "Task 2",
        _ => string.Empty
    };

    public string DurationDisplay
    {
        get
        {
            var m = DurationSeconds / 60;
            var s = DurationSeconds % 60;
            return $"{m:D2}:{s:D2}";
        }
    }

    public WritingAttemptResult(
        int attemptId, string taskCode, WritingTaskKind kind,
        string promptText, string? imagePath,
        string userText, int wordCount, int durationSeconds,
        double? bandTA, double? bandCC, double? bandLR, double? bandGRA, double? bandOverall,
        string feedbackEn, string feedbackRu,
        ObservableCollection<EssayIssueRow> issues,
        string? modelAnswerText, int modelAnswerBand)
    {
        AttemptId = attemptId;
        TaskCode = taskCode;
        Kind = kind;
        PromptText = promptText;
        ImagePath = imagePath;
        UserText = userText;
        WordCount = wordCount;
        DurationSeconds = durationSeconds;
        BandTaskAchievement = bandTA;
        BandCoherence = bandCC;
        BandLexical = bandLR;
        BandGrammar = bandGRA;
        BandOverall = bandOverall;
        FeedbackEn = feedbackEn;
        FeedbackRu = feedbackRu;
        Issues = issues;
        ModelAnswerText = modelAnswerText;
        ModelAnswerBand = modelAnswerBand;
    }

    [RelayCommand]
    private void ToggleModelAnswer() => ShowModelAnswer = !ShowModelAnswer;
}

/// <summary>One AI-flagged essay issue, with its explanation already resolved to the UI language.</summary>
public sealed record EssayIssueRow(string Category, string Quote, string Explanation, string Suggestion);
