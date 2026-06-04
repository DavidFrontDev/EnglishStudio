using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Comprehension panel (F2): loads (and caches) questions for a text, renders MCQ + open
/// questions and grades answers. Built fresh per open (transient).
/// </summary>
public partial class ComprehensionViewModel : ObservableObject
{
    private readonly IComprehensionService _service;
    private readonly ILogger<ComprehensionViewModel> _log;

    private int _textId;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _showScore;
    [ObservableProperty] private int _correctCount;
    [ObservableProperty] private int _answeredCount;
    [ObservableProperty] private int _totalCount;

    public ObservableCollection<ComprehensionQuestionViewModel> Questions { get; } = new();

    public bool HasQuestions => Questions.Count > 0;

    public string ScoreText => $"{CorrectCount} / {TotalCount} верно";

    /// <summary>Raised when the user dismisses the panel.</summary>
    public event Action? CloseRequested;

    public ComprehensionViewModel(IComprehensionService service, ILogger<ComprehensionViewModel> log)
    {
        _service = service;
        _log = log;
    }

    public async Task InitializeAsync(int textId, CancellationToken ct = default)
    {
        _textId = textId;
        IsLoading = true;
        StatusText = null;
        ShowScore = false;
        DetachQuestions();
        Questions.Clear();

        try
        {
            var dtos = await _service.GetOrGenerateAsync(textId, ct);
            foreach (var dto in dtos)
            {
                var q = new ComprehensionQuestionViewModel(dto, _service, _log);
                q.VerdictReady += OnVerdictReady;
                Questions.Add(q);
            }

            TotalCount = Questions.Count;
            if (Questions.Count == 0)
                StatusText = _service.CanUseAi
                    ? "К этому тексту не удалось составить вопросы."
                    : "Вопросы недоступны офлайн — подключите Claude CLI в настройках.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Comprehension load failed for text {TextId}", textId);
            StatusText = "Не удалось загрузить вопросы.";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasQuestions));
        }
    }

    [RelayCommand]
    private async Task CheckAll()
    {
        foreach (var q in Questions)
            if (!q.HasVerdict && q.CanCheck)
                await q.CheckCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void Close()
    {
        DetachQuestions();
        CloseRequested?.Invoke();
    }

    private void OnVerdictReady()
    {
        AnsweredCount = Questions.Count(q => q.HasVerdict);
        CorrectCount = Questions.Count(q => q.HasVerdict && q.IsCorrect);
        ShowScore = AnsweredCount > 0;
        OnPropertyChanged(nameof(ScoreText));
    }

    private void DetachQuestions()
    {
        foreach (var q in Questions)
            q.VerdictReady -= OnVerdictReady;
    }
}

/// <summary>One comprehension question (MCQ or open) with its own grading state.</summary>
public partial class ComprehensionQuestionViewModel : ObservableObject
{
    private readonly IComprehensionService _service;
    private readonly ILogger _log;
    private readonly int _correctOptionIndex;

    [ObservableProperty] private int _selectedOptionIndex = -1;
    [ObservableProperty] private string _userAnswer = string.Empty;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _hasVerdict;
    [ObservableProperty] private bool _isCorrect;
    [ObservableProperty] private double _score;
    [ObservableProperty] private string? _feedbackRu;

    public int Id { get; }
    public ComprehensionKind Kind { get; }
    public string Prompt { get; }

    /// <summary>Radio options for an MCQ (empty for open questions).</summary>
    public IReadOnlyList<McqOptionViewModel> OptionItems { get; }

    /// <summary>Shared radio group name so the options are mutually exclusive.</summary>
    public string GroupName { get; }

    public bool IsMultipleChoice => Kind == ComprehensionKind.MultipleChoice;
    public bool IsOpen => Kind == ComprehensionKind.Open;

    /// <summary>Raised after a verdict arrives so the panel can update the running score.</summary>
    public event Action? VerdictReady;

    public ComprehensionQuestionViewModel(ComprehensionQuestionDto dto, IComprehensionService service, ILogger log)
    {
        _service = service;
        _log = log;
        Id = dto.Id;
        Kind = dto.Kind;
        Prompt = dto.Prompt;
        _correctOptionIndex = dto.CorrectOptionIndex;
        GroupName = $"comp_q{dto.Id}";

        var options = new List<McqOptionViewModel>();
        for (var i = 0; i < dto.Options.Count; i++)
            options.Add(new McqOptionViewModel(i, dto.Options[i], OnOptionSelected));
        OptionItems = options;
    }

    private void OnOptionSelected(int index) => SelectedOptionIndex = index;

    /// <summary>The user has supplied an answer that can be graded.</summary>
    public bool CanCheck => IsMultipleChoice ? SelectedOptionIndex >= 0 : !string.IsNullOrWhiteSpace(UserAnswer);

    [RelayCommand]
    private async Task Check()
    {
        if (IsChecking || !CanCheck) return;

        // Answer encoding shared with the service: MCQ = selected index as an invariant
        // string; Open = the raw text.
        var answer = IsMultipleChoice
            ? SelectedOptionIndex.ToString(CultureInfo.InvariantCulture)
            : UserAnswer.Trim();

        IsChecking = true;
        try
        {
            var verdict = await _service.EvaluateAsync(Id, answer);
            IsCorrect = verdict.IsCorrect;
            Score = verdict.Score;
            FeedbackRu = verdict.FeedbackRu;
            HasVerdict = true;
            VerdictReady?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Grading comprehension question {Id} failed", Id);
            FeedbackRu = "Не удалось проверить ответ.";
            HasVerdict = true;
            IsCorrect = false;
            VerdictReady?.Invoke();
        }
        finally
        {
            IsChecking = false;
        }
    }
}

/// <summary>One selectable MCQ option. Reports its index up when the radio is checked.</summary>
public partial class McqOptionViewModel : ObservableObject
{
    private readonly Action<int> _onSelected;

    [ObservableProperty] private bool _isSelected;

    public int Index { get; }
    public string Text { get; }

    public McqOptionViewModel(int index, string text, Action<int> onSelected)
    {
        Index = index;
        Text = text;
        _onSelected = onSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(Index);
    }
}
