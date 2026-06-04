using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>Content of the reader's translation popup for one selected word/phrase.</summary>
public partial class LookupPopupViewModel : ObservableObject
{
    private readonly ISrsService _srs;
    private readonly IReadingPracticeService _practice;
    private readonly ILogger _log;

    private int? _wordId;
    private int _textId;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _found;
    [ObservableProperty] private string? _ipa;
    [ObservableProperty] private string? _partOfSpeechRu;
    [ObservableProperty] private string _translationsLine = string.Empty;
    [ObservableProperty] private string? _definitionRu;
    [ObservableProperty] private bool _isAiGenerated;
    [ObservableProperty] private bool _isPhrase;
    [ObservableProperty] private string? _message;

    [ObservableProperty] private bool _canAddToTraining;
    [ObservableProperty] private bool _addedToTraining;

    // Add the looked-up word to THIS text's practice pool (feeds the text-scoped FSRS trainer).
    [ObservableProperty] private bool _canAddToPool;
    [ObservableProperty] private bool _addedToPool;

    // Highlight state for the current selection (managed by the reader as the selection changes).
    [ObservableProperty] private bool _canHighlight;
    [ObservableProperty] private bool _isSelectionHighlighted;

    public LookupPopupViewModel(ISrsService srs, IReadingPracticeService practice, ILogger log)
    {
        _srs = srs;
        _practice = practice;
        _log = log;
    }

    /// <summary>Binds the popup to the text being read (for the per-text practice pool).</summary>
    public void SetContext(int textId) => _textId = textId;

    /// <summary>Resets to the "looking up…" state for a fresh selection.</summary>
    public void BeginLookup(string query)
    {
        Query = query;
        IsLoading = true;
        Found = false;
        Message = null;
        Ipa = null;
        PartOfSpeechRu = null;
        TranslationsLine = string.Empty;
        DefinitionRu = null;
        IsAiGenerated = false;
        IsPhrase = false;
        CanAddToTraining = false;
        AddedToTraining = false;
        CanAddToPool = false;
        AddedToPool = false;
        _wordId = null;
    }

    public async Task ApplyAsync(WordLookupResult r, bool canEnrich)
    {
        IsLoading = false;
        Query = string.IsNullOrWhiteSpace(r.Query) ? Query : r.Query;
        Found = r.Found;
        IsPhrase = r.IsPhrase;
        IsAiGenerated = r.IsAiGenerated;

        if (!r.Found)
        {
            Message = canEnrich
                ? "Перевод не найден."
                : "Нет в словаре. Claude CLI недоступен — подключите его в настройках для автоперевода.";
            return;
        }

        Ipa = r.Ipa;
        PartOfSpeechRu = r.PartOfSpeechRu;
        TranslationsLine = string.Join(";  ", r.TranslationsRu);
        DefinitionRu = r.DefinitionRu;
        _wordId = r.WordId;

        if (_wordId is int id)
        {
            try
            {
                AddedToTraining = await _srs.IsInTrainingForWordAsync(id);
                CanAddToTraining = !AddedToTraining;

                AddedToPool = await _practice.IsInPoolAsync(_textId, id);
                CanAddToPool = !AddedToPool;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to check training/pool state for word {Id}", id);
            }
        }
    }

    [RelayCommand]
    private async Task AddToTraining()
    {
        if (_wordId is not int id) return;
        try
        {
            await _srs.AddWordAsync(id);
            AddedToTraining = true;
            CanAddToTraining = false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to add word {Id} to training", id);
        }
    }

    /// <summary>Adds the word to THIS text's practice pool (and ensures it's an FSRS card).</summary>
    [RelayCommand]
    private async Task AddToPool()
    {
        if (_wordId is not int id) return;
        try
        {
            await _srs.AddWordAsync(id);                       // so the text-scoped session finds a card
            await _practice.AddToPoolAsync(_textId, id, Query);
            AddedToPool = true;
            CanAddToPool = false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to add word {Id} to the text {TextId} pool", id, _textId);
        }
    }
}
