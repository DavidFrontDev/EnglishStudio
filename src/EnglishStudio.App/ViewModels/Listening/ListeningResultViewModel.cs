using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Ai.Reports;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Listening;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Listening;

public partial class ListeningResultViewModel : ObservableObject
{
    private readonly IListeningTestService _testSvc;
    private readonly ListeningFeedbackService _feedback;
    private readonly IClaudeCliClient _cli;
    private readonly ILogger<ListeningResultViewModel> _log;

    private int _attemptId;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private int _rawScore;
    [ObservableProperty] private int _totalQuestions;
    [ObservableProperty] private double _bandEstimate;
    [ObservableProperty] private string _bandLabel = string.Empty;
    [ObservableProperty] private string _durationLabel = string.Empty;
    [ObservableProperty] private bool _isTrainingMode;

    public ObservableCollection<ListeningAnswerRow> Answers { get; } = new();
    public ObservableCollection<BreakdownRow> PartBreakdown { get; } = new();
    public ObservableCollection<BreakdownRow> TypeBreakdown { get; } = new();
    public ObservableCollection<AiInsightRow> AiPartInsights { get; } = new();
    public ObservableCollection<AiQuestionRow> AiQuestionExplanations { get; } = new();
    public ObservableCollection<string> AiTips { get; } = new();

    [ObservableProperty] private string _aiSummaryRu = string.Empty;
    [ObservableProperty] private bool _hasAiFeedback;
    [ObservableProperty] private bool _isLoadingAi;
    [ObservableProperty] private string _aiStatus = string.Empty;

    public bool IsAiAvailable => _cli.IsAvailable;

    public ListeningResultViewModel(
        IListeningTestService testSvc,
        ListeningFeedbackService feedback,
        IClaudeCliClient cli,
        ILogger<ListeningResultViewModel> log)
    {
        _testSvc = testSvc;
        _feedback = feedback;
        _cli = cli;
        _log = log;
    }

    public async Task LoadAsync(int attemptId)
    {
        _attemptId = attemptId;
        var attempt = await _testSvc.GetAttemptAsync(attemptId);
        if (attempt is null) return;

        Title = attempt.TestSet.Title;
        RawScore = attempt.RawScore;
        TotalQuestions = attempt.TestSet.Parts.Sum(p => p.Questions.Count);
        BandEstimate = attempt.BandEstimate;
        BandLabel = BandEstimate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        IsTrainingMode = attempt.IsTrainingMode;
        DurationLabel = $"⏱ Время прохождения: {attempt.DurationSeconds / 60} мин {attempt.DurationSeconds % 60} с";

        var byQid = attempt.Answers
            .GroupBy(a => a.TestQuestionId)
            .ToDictionary(g => g.Key, g => g.First());

        Answers.Clear();
        PartBreakdown.Clear();
        TypeBreakdown.Clear();

        // Per-question rows + collect part/type breakdown totals in one pass.
        var typeBuckets = new Dictionary<QuestionType, (int correct, int total)>();
        foreach (var part in attempt.TestSet.Parts.OrderBy(p => p.OrderInTest))
        {
            var partCorrect = 0;
            var partTotal = 0;
            foreach (var q in part.Questions.OrderBy(qq => qq.OrderInPart))
            {
                byQid.TryGetValue(q.Id, out var ans);
                var userAnswer = ans is not null ? Strip(ans.UserAnswerJson) : string.Empty;
                var isCorrect = ans?.IsCorrect ?? false;

                Answers.Add(new ListeningAnswerRow(
                    q.OrderInPart,
                    string.IsNullOrWhiteSpace(userAnswer) ? "—" : userAnswer,
                    Strip(q.AnswerKeyJson),
                    isCorrect));

                partTotal++;
                if (isCorrect) partCorrect++;

                var typeKey = q.Type;
                if (!typeBuckets.TryGetValue(typeKey, out var bucket)) bucket = (0, 0);
                bucket = (bucket.correct + (isCorrect ? 1 : 0), bucket.total + 1);
                typeBuckets[typeKey] = bucket;
            }
            PartBreakdown.Add(new BreakdownRow(
                Label: $"Part {part.OrderInTest} — {part.Title}",
                Correct: partCorrect,
                Total: partTotal));
        }
        foreach (var (type, b) in typeBuckets.OrderByDescending(kv => kv.Value.total))
        {
            TypeBreakdown.Add(new BreakdownRow(
                Label: HumanizeType(type),
                Correct: b.correct,
                Total: b.total));
        }

        // Load any previously saved AI report so the user doesn't pay for a re-run.
        AiStatus = string.Empty;
        var saved = await _feedback.LoadSavedAsync(attemptId);
        ApplyReport(saved);
        if (saved is null)
        {
            AiStatus = IsAiAvailable
                ? "Нажмите «Разбор от AI», чтобы получить объяснение ошибок и советы."
                : "Claude CLI не настроен — раздел доступен после установки.";
        }
        EvaluateAiCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanEvaluateAi))]
    private async Task EvaluateAiAsync()
    {
        if (_attemptId == 0) return;
        IsLoadingAi = true;
        AiStatus = "Claude обрабатывает результат — это занимает 1–3 минуты…";
        EvaluateAiCommand.NotifyCanExecuteChanged();
        try
        {
            var report = await _feedback.EvaluateAndSaveAsync(_attemptId);
            ApplyReport(report);
            if (report is null)
                AiStatus = "Claude CLI вернул пустой / некорректный ответ. Попробуйте ещё раз.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Listening AI evaluation failed for attempt {Id}", _attemptId);
            AiStatus = "Ошибка при вызове Claude CLI. Подробности — в логах.";
        }
        finally
        {
            IsLoadingAi = false;
            EvaluateAiCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanEvaluateAi() => !IsLoadingAi && IsAiAvailable;

    private void ApplyReport(ListeningScoreReport? report)
    {
        AiPartInsights.Clear();
        AiQuestionExplanations.Clear();
        AiTips.Clear();
        if (report is null)
        {
            AiSummaryRu = string.Empty;
            HasAiFeedback = false;
            return;
        }
        AiSummaryRu = report.SummaryRu;
        foreach (var pi in report.PartInsights)
            AiPartInsights.Add(new AiInsightRow(pi.PartNumber, pi.PartTitle, pi.CommentRu));
        foreach (var qe in report.QuestionExplanations)
            AiQuestionExplanations.Add(new AiQuestionRow(qe.QuestionNumber, qe.UserAnswer, qe.CorrectAnswer, qe.ExplanationRu));
        foreach (var t in report.TipsRu) AiTips.Add(t);
        HasAiFeedback = true;
        AiStatus = "Разбор готов. Источник: Claude CLI.";
    }

    private static string Strip(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        var trimmed = json.Trim();
        if (trimmed.StartsWith('"'))
        {
            try { return JsonSerializer.Deserialize<string>(trimmed) ?? trimmed; }
            catch (JsonException) { return trimmed.Trim('"'); }
        }
        return trimmed;
    }

    private static string HumanizeType(QuestionType t) => t switch
    {
        QuestionType.NoteCompletion => "Заметки (note completion)",
        QuestionType.SummaryCompletion => "Summary completion",
        QuestionType.SentenceCompletion => "Sentence completion",
        QuestionType.TableCompletion => "Таблица (table)",
        QuestionType.FlowChartCompletion => "Каскад (flow chart)",
        QuestionType.MultipleChoiceSingle => "MCQ (один ответ)",
        QuestionType.MultipleChoiceMulti => "MCQ (несколько ответов)",
        QuestionType.MatchingFeatures => "Сопоставление (matching)",
        QuestionType.MatchingHeadings => "Подбор заголовков",
        QuestionType.MatchingInformation => "Сопоставление информации",
        QuestionType.MatchingSentenceEndings => "Окончания предложений",
        QuestionType.ShortAnswer => "Короткий ответ",
        QuestionType.TrueFalseNotGiven => "True / False / Not Given",
        QuestionType.YesNoNotGiven => "Yes / No / Not Given",
        _ => t.ToString()
    };
}

public sealed record ListeningAnswerRow(int Number, string UserAnswer, string CorrectAnswer, bool IsCorrect);

public sealed record BreakdownRow(string Label, int Correct, int Total)
{
    public string Score => $"{Correct} / {Total}";
    public double Percent => Total > 0 ? (double)Correct / Total : 0;
    public string PercentLabel => $"{Math.Round(Percent * 100)}%";
}

public sealed record AiInsightRow(int PartNumber, string PartTitle, string CommentRu)
{
    public string Heading => $"Part {PartNumber} — {PartTitle}";
}

public sealed record AiQuestionRow(int QuestionNumber, string UserAnswer, string CorrectAnswer, string ExplanationRu)
{
    public string Heading => $"Q{QuestionNumber}: ваш «{UserAnswer}» → правильный «{CorrectAnswer}»";
}
