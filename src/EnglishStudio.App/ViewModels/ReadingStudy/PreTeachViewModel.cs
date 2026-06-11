using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Pre-teach panel (F1): lists a text's unfamiliar words, lets the user pick which to learn
/// and pushes them into SRS before reading. Built fresh per open (transient).
/// </summary>
public partial class PreTeachViewModel : ObservableObject
{
    private readonly IPreTeachService _service;
    private readonly ILogger<PreTeachViewModel> _log;

    private int _textId;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _addProgress;
    [ObservableProperty] private bool _canEnrich = true;
    [ObservableProperty] private int _totalDistinctWords;
    [ObservableProperty] private int _knownCount;
    [ObservableProperty] private int _addedCount;

    public ObservableCollection<PreTeachCandidateViewModel> Candidates { get; } = new();

    public bool HasCandidates => Candidates.Count > 0;

    /// <summary>Raised when the user dismisses the panel (skip / start reading).</summary>
    public event Action? CloseRequested;

    public PreTeachViewModel(IPreTeachService service, ILogger<PreTeachViewModel> log)
    {
        _service = service;
        _log = log;
    }

    public async Task InitializeAsync(int textId, CancellationToken ct = default)
    {
        _textId = textId;
        IsLoading = true;
        IsDone = false;
        StatusText = null;
        AddProgress = null;
        Candidates.Clear();

        try
        {
            CanEnrich = _service.CanEnrich;
            var result = await _service.AnalyzeAsync(textId, options: null, ct);

            TotalDistinctWords = result.TotalDistinctWords;
            KnownCount = result.KnownCount;

            foreach (var c in result.Candidates)
                Candidates.Add(new PreTeachCandidateViewModel(c));

            if (Candidates.Count == 0)
                StatusText = Loc.Tr("ReadStudy_PreTeachNoUnknown");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pre-teach analysis failed for text {TextId}", textId);
            StatusText = Loc.Tr("ReadStudy_PreTeachAnalyzeFailed");
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasCandidates));
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var c in Candidates)
            if (c.CanSelect) c.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var c in Candidates)
            c.IsSelected = false;
    }

    [RelayCommand]
    private async Task StudySelected()
    {
        if (IsAdding) return;

        var chosen = Candidates
            .Where(c => c.IsSelected && c.CanSelect)
            .Select(c => c.Model)
            .ToList();

        if (chosen.Count == 0)
        {
            StatusText = Loc.Tr("ReadStudy_PreTeachSelectAtLeastOne");
            return;
        }

        IsAdding = true;
        AddProgress = null;
        try
        {
            var progress = new Progress<string>(s => AddProgress = s);
            AddedCount = await _service.AddToTrainingAsync(chosen, progress);
            IsDone = true;
            StatusText = Loc.Format("ReadStudy_PreTeachAddedCount", AddedCount);
        }
        catch (OperationCanceledException) { /* panel closing */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Pre-teach add-to-training failed");
            StatusText = Loc.Tr("ReadStudy_PreTeachAddFailed");
        }
        finally
        {
            IsAdding = false;
            AddProgress = null;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}

/// <summary>One pre-teach candidate with a learn/skip checkbox.</summary>
public partial class PreTeachCandidateViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public PreTeachCandidate Model { get; }

    public PreTeachCandidateViewModel(PreTeachCandidate model)
    {
        Model = model;
        // Words already in SRS can't be re-added; everything else is checked by default.
        _isSelected = !model.AlreadyInTraining;
    }

    public string Headword => Model.Headword;
    public CefrLevel Cefr => Model.Cefr;
    public int Occurrences => Model.Occurrences;

    /// <summary>Translation, or a placeholder when the word isn't in the dictionary yet.</summary>
    public string TranslationDisplay =>
        !string.IsNullOrWhiteSpace(Model.TranslationRu)
            ? Model.TranslationRu!
            : (Model.InDictionary ? Loc.Tr("ReadStudy_PreTeachNoTranslation") : Loc.Tr("ReadStudy_PreTeachAiWillAdd"));

    /// <summary>Not in the dictionary yet → will be AI-enriched on add.</summary>
    public bool IsAiWord => !Model.InDictionary;

    public bool AlreadyInTraining => Model.AlreadyInTraining;

    /// <summary>Already-learned words are shown but can't be toggled on.</summary>
    public bool CanSelect => !Model.AlreadyInTraining;
}
