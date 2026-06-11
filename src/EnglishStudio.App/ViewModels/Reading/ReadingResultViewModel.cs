using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Ielts.Core.Entities;
using EnglishStudio.Modules.Ielts.Reading;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Reading;

public partial class ReadingResultViewModel : ObservableObject
{
    private readonly IReadingTestService _testSvc;
    private readonly ILogger<ReadingResultViewModel> _log;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private int _rawScore;
    [ObservableProperty] private int _totalQuestions;
    [ObservableProperty] private double _bandEstimate;
    [ObservableProperty] private string _bandLabel = string.Empty;
    [ObservableProperty] private string _durationLabel = string.Empty;
    [ObservableProperty] private bool _isTrainingMode;
    [ObservableProperty] private string _statusText = string.Empty;

    public ObservableCollection<QuestionResultRow> Wrong { get; } = new();
    public ObservableCollection<TypeBreakdownRow> Breakdown { get; } = new();

    public ReadingResultViewModel(IReadingTestService testSvc, ILogger<ReadingResultViewModel> log)
    {
        _testSvc = testSvc;
        _log = log;
    }

    public async Task LoadAsync(int attemptId)
    {
        StatusText = string.Empty;
        try
        {
            await LoadCoreAsync(attemptId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load reading result {Id}", attemptId);
            StatusText = Loc.Tr("ReadIelts_LoadTestsFailed");
        }
    }

    private async Task LoadCoreAsync(int attemptId)
    {
        var attempt = await _testSvc.GetAttemptAsync(attemptId);
        if (attempt is null) return;

        Title = attempt.TestSet.Title;
        RawScore = attempt.RawScore;
        TotalQuestions = attempt.TestSet.Parts.Sum(p => p.Questions.Count);
        BandEstimate = attempt.BandEstimate;
        BandLabel = BandEstimate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        IsTrainingMode = attempt.IsTrainingMode;
        DurationLabel = Loc.Format("ReadIelts_DurationLabel", attempt.DurationSeconds / 60, attempt.DurationSeconds % 60);

        // Defensive: tolerate legacy duplicate TestAnswer rows (pre-unique-index) by keeping
        // the first answer per question rather than throwing on ToDictionary.
        var byQid = attempt.Answers
            .GroupBy(a => a.TestQuestionId)
            .ToDictionary(g => g.Key, g => g.First());
        var displayCounter = 1;

        Wrong.Clear();
        Breakdown.Clear();
        var typeStats = new Dictionary<QuestionType, (int Correct, int Total)>();

        foreach (var part in attempt.TestSet.Parts.OrderBy(p => p.OrderInTest))
        {
            foreach (var q in part.Questions.OrderBy(qq => qq.OrderInPart))
            {
                byQid.TryGetValue(q.Id, out var ans);
                var userAnswer = ans is not null ? Strip(ans.UserAnswerJson) : Loc.Tr("ReadIelts_NoAnswer");
                var correctAnswer = Strip(q.AnswerKeyJson);
                var isCorrect = ans?.IsCorrect ?? false;

                var current = typeStats.TryGetValue(q.Type, out var prev) ? prev : (Correct: 0, Total: 0);
                typeStats[q.Type] = (current.Correct + (isCorrect ? 1 : 0), current.Total + 1);

                if (!isCorrect)
                {
                    Wrong.Add(new QuestionResultRow(
                        displayCounter,
                        part.Title,
                        q.Stem,
                        userAnswer,
                        correctAnswer));
                }
                displayCounter++;
            }
        }

        foreach (var (type, stats) in typeStats.OrderByDescending(kv => kv.Value.Total))
        {
            Breakdown.Add(new TypeBreakdownRow(FormatType(type), stats.Correct, stats.Total));
        }
    }

    private static string Strip(string json)
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

    private static string FormatType(QuestionType t) => t switch
    {
        QuestionType.TrueFalseNotGiven => "True/False/Not Given",
        QuestionType.YesNoNotGiven => "Yes/No/Not Given",
        QuestionType.MultipleChoiceSingle => "Multiple Choice (single)",
        QuestionType.MultipleChoiceMulti => "Multiple Choice (multi)",
        QuestionType.MatchingHeadings => "Matching Headings",
        QuestionType.MatchingInformation => "Matching Information",
        QuestionType.MatchingFeatures => "Matching Features",
        QuestionType.MatchingSentenceEndings => "Matching Sentence Endings",
        QuestionType.SentenceCompletion => "Sentence Completion",
        QuestionType.SummaryCompletion => "Summary Completion",
        QuestionType.NoteCompletion => "Note Completion",
        QuestionType.TableCompletion => "Table Completion",
        QuestionType.FlowChartCompletion => "Flow Chart Completion",
        QuestionType.ShortAnswer => "Short Answer",
        _ => t.ToString()
    };
}

public sealed record QuestionResultRow(
    int Number,
    string PartTitle,
    string Stem,
    string UserAnswer,
    string CorrectAnswer);

public sealed record TypeBreakdownRow(string TypeLabel, int Correct, int Total)
{
    public string Label => $"{Correct} / {Total}";
    public double Ratio => Total == 0 ? 0 : (double)Correct / Total;
}
