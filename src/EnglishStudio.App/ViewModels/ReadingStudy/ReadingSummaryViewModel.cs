using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Post-read summary panel: speed (this run + average), time, accuracy (when the Whisper
/// analysis ran) and the list of difficult words, each addable to SRS. Also surfaces a short
/// history / WPM trend for the text. Built fresh per finished session (transient).
/// </summary>
public partial class ReadingSummaryViewModel : ObservableObject
{
    private readonly ITextLookupService _lookup;
    private readonly ISrsService _srs;
    private readonly IPhonemeFeedbackService _phonemes;
    private readonly IReadingSessionService _sessions;
    private readonly ILogger<ReadingSummaryViewModel> _log;

    private int _textId;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private double _wpm;
    [ObservableProperty] private double _averageWpm;
    [ObservableProperty] private bool _hasAverage;
    [ObservableProperty] private int _wordsRead;
    [ObservableProperty] private bool _completed;
    [ObservableProperty] private string _durationText = "0:00";

    [ObservableProperty] private bool _accuracyAvailable;
    [ObservableProperty] private double _accuracyPct;
    [ObservableProperty] private int _wordsSkipped;

    [ObservableProperty] private bool _hasDifficultWords;
    [ObservableProperty] private bool _hasHistory;

    /// <summary>Difficult words from the analysis, each with a "в изучение" action.</summary>
    public ObservableCollection<DifficultWordViewModel> DifficultWords { get; } = new();

    /// <summary>Previous sessions for this text (most recent first), for the WPM trend.</summary>
    public ObservableCollection<ReadingHistoryItem> History { get; } = new();

    /// <summary>Raised when the user dismisses the summary.</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when the user wants to answer comprehension questions (F2).</summary>
    public event Action? QuestionsRequested;

    public ReadingSummaryViewModel(
        ITextLookupService lookup,
        ISrsService srs,
        IPhonemeFeedbackService phonemes,
        IReadingSessionService sessions,
        ILogger<ReadingSummaryViewModel> log)
    {
        _lookup = lookup;
        _srs = srs;
        _phonemes = phonemes;
        _sessions = sessions;
        _log = log;
    }

    public Task InitializeAsync(
        int textId,
        string title,
        ReadingRunResult run,
        ReadingAnalysis? analysis,
        IReadOnlyList<TextToken> tokens,
        IReadOnlyList<ReadingSessionSummary> history)
    {
        _textId = textId;
        Title = title;
        Wpm = run.Wpm;
        WordsRead = run.WordsRead;
        Completed = run.Completed;
        DurationText = FormatDuration(run.ElapsedSec);

        AccuracyAvailable = analysis is not null;
        AccuracyPct = analysis?.AccuracyPct ?? 0;
        WordsSkipped = analysis?.WordsSkipped ?? 0;

        BuildDifficultWords(analysis, tokens);
        BuildHistory(history, run.Wpm);

        return Task.CompletedTask;
    }

    private void BuildDifficultWords(ReadingAnalysis? analysis, IReadOnlyList<TextToken> tokens)
    {
        DifficultWords.Clear();
        if (analysis is null) { HasDifficultWords = false; return; }

        // Map word-index → display text (the same WordIndex space the reader & cursor use).
        var byWordIndex = new Dictionary<int, string>();
        foreach (var t in tokens)
            if (t.Kind == TokenKind.Word && t.WordIndex is int wi)
                byWordIndex[wi] = t.Text;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var idx in analysis.DifficultWordIndices)
        {
            if (!byWordIndex.TryGetValue(idx, out var raw)) continue;
            var key = ReadingTokenizer.NormalizeWord(raw);
            if (key.Length == 0 || !seen.Add(key)) continue;

            DifficultWords.Add(new DifficultWordViewModel(raw, _lookup, _srs, _phonemes, _log));
            if (DifficultWords.Count >= 40) break; // keep the panel sane on rough reads
        }

        HasDifficultWords = DifficultWords.Count > 0;
    }

    private void BuildHistory(IReadOnlyList<ReadingSessionSummary> history, double thisRunWpm)
    {
        History.Clear();

        var completedWpms = new List<double>();
        // history is "most recent first"; show as-is.
        foreach (var s in history)
        {
            History.Add(new ReadingHistoryItem(
                s.StartedAt.ToLocalTime(),
                s.Wpm,
                s.AccuracyPct,
                s.Completed));
            if (s.Completed && s.Wpm > 0) completedWpms.Add(s.Wpm);
        }

        HasHistory = History.Count > 0;

        if (completedWpms.Count > 0)
        {
            AverageWpm = completedWpms.Average();
            HasAverage = completedWpms.Count >= 1;
        }
        else
        {
            AverageWpm = thisRunWpm;
            HasAverage = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void Questions() => QuestionsRequested?.Invoke();

    /// <summary>Wipes this text's reading history (with confirmation). Resets the list and average.</summary>
    [RelayCommand]
    private async Task ClearHistory()
    {
        var ok = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("ReadStudy_ClearHistoryTitle"),
            Loc.Tr("ReadStudy_ClearHistoryBody"),
            confirmText: Loc.Tr("ReadStudy_ClearHistoryConfirm"),
            icon: "🗑");
        if (!ok) return;

        try
        {
            await _sessions.ClearByTextAsync(_textId);
            History.Clear();
            HasHistory = false;
            // No completed sessions remain — fall back to this run's WPM, hide the "average" line.
            AverageWpm = Wpm;
            HasAverage = false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to clear reading history for text {TextId}", _textId);
        }
    }

    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }
}

/// <summary>One past reading session, for the summary's history / trend list.</summary>
public sealed record ReadingHistoryItem(DateTime StartedAt, double Wpm, double AccuracyPct, bool Completed)
{
    public string WhenText => StartedAt.ToString("dd.MM HH:mm");
    public string WpmText => Loc.Format("ReadStudy_WpmText", (int)Wpm);
}

/// <summary>A difficult word in the summary with a one-tap "add to study" (SRS) action and an
/// expandable phoneme/IPA breakdown (F3).</summary>
public partial class DifficultWordViewModel : ObservableObject
{
    private readonly ITextLookupService _lookup;
    private readonly ISrsService _srs;
    private readonly IPhonemeFeedbackService _phonemes;
    private readonly ILogger _log;

    [ObservableProperty] private bool _canAdd = true;
    [ObservableProperty] private bool _added;
    [ObservableProperty] private bool _busy;

    [ObservableProperty] private bool _isGuideVisible;
    [ObservableProperty] private PhonemeGuideViewModel? _guide;

    public string Word { get; }

    public DifficultWordViewModel(string word, ITextLookupService lookup, ISrsService srs, IPhonemeFeedbackService phonemes, ILogger log)
    {
        Word = word;
        _lookup = lookup;
        _srs = srs;
        _phonemes = phonemes;
        _log = log;
    }

    /// <summary>Toggles the phoneme breakdown, building it lazily on first open.</summary>
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

    [RelayCommand]
    private async Task AddToTraining()
    {
        if (Added || Busy) return;
        Busy = true;
        try
        {
            var result = await _lookup.LookupAsync(Word);
            if (result.Found && result.WordId is int id)
            {
                await _srs.AddWordAsync(id);
                Added = true;
                CanAdd = false;
            }
            else
            {
                // Not in the dictionary and not enrichable — nothing to add.
                CanAdd = false;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to add difficult word '{Word}' to training", Word);
        }
        finally
        {
            Busy = false;
        }
    }
}
