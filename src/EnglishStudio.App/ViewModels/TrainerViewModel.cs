using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Localization;
using EnglishStudio.Modules.Dictionary.Audio;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Images;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading.Services;

namespace EnglishStudio.App.ViewModels;

public partial class TrainerViewModel : ObservableObject
{
    private readonly ISrsService _srs;
    private readonly IAudioCacheService _audioCache;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IAppSettings _settings;
    private readonly IReadingPracticeService _practice;

    private readonly Queue<TrainerCardViewModel> _sessionQueue = new();

    private bool _isRating;

    /// <summary>Reading texts that have a practice pool — each can be drilled on demand.</summary>
    public ObservableCollection<TextPoolItem> Pools { get; } = new();

    [ObservableProperty]
    private bool _hasPools;

    [ObservableProperty]
    private TrainerCardViewModel? _currentCard;

    [ObservableProperty]
    private bool _isAnswerRevealed;

    [ObservableProperty]
    private int _sessionDone;

    [ObservableProperty]
    private int _sessionTotal;

    [ObservableProperty]
    private bool _isSessionActive;

    [ObservableProperty]
    private string? _sessionStatus;

    [ObservableProperty]
    private bool _isAudioBusy;

    public TrainerViewModel(
        ISrsService srs,
        IAudioCacheService audioCache,
        IAudioPlayer audioPlayer,
        IAppSettings settings,
        IReadingPracticeService practice)
    {
        _srs = srs;
        _audioCache = audioCache;
        _audioPlayer = audioPlayer;
        _settings = settings;
        _practice = practice;

        _ = LoadPoolsAsync();
    }

    /// <summary>Refreshes the list of reading-text practice pools (titles + counts).</summary>
    [RelayCommand]
    private async Task LoadPoolsAsync()
    {
        try
        {
            var pools = await _practice.ListPoolsAsync();
            Pools.Clear();
            foreach (var p in pools) Pools.Add(new TextPoolItem(p.ReadingTextId, p.Title, p.Count));
            HasPools = Pools.Count > 0;
        }
        catch
        {
            // ignore — pools panel just stays as-is
        }
    }

    /// <summary>Drills a single text's practice pool (all its cards) through the FSRS loop.</summary>
    [RelayCommand]
    private async Task StartPoolSessionAsync(int textId)
    {
        var wordIds = await _practice.ListPoolWordIdsAsync(textId);
        var queue = await _srs.BuildSessionForWordIdsAsync(wordIds, DateTime.UtcNow);

        _sessionQueue.Clear();
        foreach (var p in queue)
        {
            var card = ToCard(p);
            if (card is not null) _sessionQueue.Enqueue(card);
        }

        SessionTotal = _sessionQueue.Count;
        SessionDone = 0;
        IsSessionActive = SessionTotal > 0;

        if (SessionTotal == 0)
        {
            SessionStatus = Loc.Tr("Trainer_PoolNoCards");
            CurrentCard = null;
            return;
        }

        SessionStatus = null;
        Advance();
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        var maxNew = _settings.DailyNewLimit;
        var maxReview = _settings.DailyReviewLimit;

        var queue = await _srs.BuildSessionAsync(maxNew, maxReview, DateTime.UtcNow);
        _sessionQueue.Clear();
        foreach (var p in queue)
        {
            var card = ToCard(p);
            if (card is not null) _sessionQueue.Enqueue(card);
        }

        SessionTotal = _sessionQueue.Count;
        SessionDone = 0;
        IsSessionActive = SessionTotal > 0;

        if (SessionTotal == 0)
        {
            SessionStatus = Loc.Tr("Trainer_NoCards");
            CurrentCard = null;
            return;
        }

        SessionStatus = null;
        Advance();
    }

    [RelayCommand]
    private void RevealAnswer() => IsAnswerRevealed = true;

    [RelayCommand(CanExecute = nameof(CanRate))]
    private Task RateAgainAsync() => RateAsync(SrsRating.Again);

    [RelayCommand(CanExecute = nameof(CanRate))]
    private Task RateHardAsync() => RateAsync(SrsRating.Hard);

    [RelayCommand(CanExecute = nameof(CanRate))]
    private Task RateGoodAsync() => RateAsync(SrsRating.Good);

    [RelayCommand(CanExecute = nameof(CanRate))]
    private Task RateEasyAsync() => RateAsync(SrsRating.Easy);

    private bool CanRate() => !_isRating;

    private void NotifyRateCommands()
    {
        RateAgainCommand.NotifyCanExecuteChanged();
        RateHardCommand.NotifyCanExecuteChanged();
        RateGoodCommand.NotifyCanExecuteChanged();
        RateEasyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void EndSession()
    {
        _sessionQueue.Clear();
        CurrentCard = null;
        IsAnswerRevealed = false;
        IsSessionActive = false;
        SessionStatus = Loc.Tr("Trainer_SessionEnded");
        _ = LoadPoolsAsync();
    }

    [RelayCommand]
    private async Task PlayUkAsync()
    {
        if (CurrentCard is { HasAudioUk: true, Kind: TrainerCardKind.Word, OwnerId: var id })
        {
            IsAudioBusy = true;
            try
            {
                var path = await _audioCache.GetOrFetchAsync(id, AudioVariant.Uk);
                if (!string.IsNullOrEmpty(path)) _audioPlayer.Play(path);
            }
            finally { IsAudioBusy = false; }
        }
    }

    [RelayCommand]
    private async Task PlayUsAsync()
    {
        if (CurrentCard is { HasAudioUs: true, Kind: TrainerCardKind.Word, OwnerId: var id })
        {
            IsAudioBusy = true;
            try
            {
                var path = await _audioCache.GetOrFetchAsync(id, AudioVariant.Us);
                if (!string.IsNullOrEmpty(path)) _audioPlayer.Play(path);
            }
            finally { IsAudioBusy = false; }
        }
    }

    private async Task RateAsync(SrsRating rating)
    {
        if (CurrentCard is null || _isRating) return;
        _isRating = true;
        NotifyRateCommands();
        try
        {
            try
            {
                await _srs.RateAsync(CurrentCard.ProgressId, rating, DateTime.UtcNow);
            }
            catch (Exception)
            {
                // swallow — UI flow continues
            }
            SessionDone++;
            Advance();
        }
        finally
        {
            _isRating = false;
            NotifyRateCommands();
        }
    }

    private void Advance()
    {
        IsAnswerRevealed = false;
        if (_sessionQueue.Count == 0)
        {
            CurrentCard = null;
            IsSessionActive = false;
            SessionStatus = Loc.Format("Trainer_SessionDone", SessionDone);
            return;
        }
        CurrentCard = _sessionQueue.Dequeue();
    }

    private static TrainerCardViewModel? ToCard(UserWordProgress p)
    {
        if (p.Word is { } word)
        {
            return new TrainerCardViewModel
            {
                ProgressId = p.Id,
                Kind = TrainerCardKind.Word,
                OwnerId = word.Id,
                Headword = word.Headword,
                PartOfSpeechCode = word.PartOfSpeech.Code,
                PartOfSpeechNameRu = word.PartOfSpeech.NameRu,
                IpaUk = word.IpaUk,
                IpaUs = word.IpaUs,
                HasAudioUk = !string.IsNullOrEmpty(word.AudioUkPath),
                HasAudioUs = !string.IsNullOrEmpty(word.AudioUsPath),
                TranslationsRu = word.Senses
                    .OrderBy(s => s.OrderIndex)
                    .SelectMany(s => s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu))
                    .Distinct()
                    .Take(6)
                    .ToList(),
                DefinitionRu = word.Senses.OrderBy(s => s.OrderIndex).FirstOrDefault()?.DefinitionRu,
                DefinitionEn = word.Senses.OrderBy(s => s.OrderIndex).FirstOrDefault()?.DefinitionEn,
                Examples = word.Examples
                    .Where(e => !string.IsNullOrWhiteSpace(e.TextEn))
                    .Take(3)
                    .Select(e => new ExampleDetail { TextEn = e.TextEn, TextRu = e.TextRu, Source = e.Source })
                    .ToList(),
            };
        }
        if (p.PhrasalVerb is { } pv)
        {
            return new TrainerCardViewModel
            {
                ProgressId = p.Id,
                Kind = TrainerCardKind.PhrasalVerb,
                OwnerId = pv.Id,
                Headword = pv.Headword,
                PartOfSpeechCode = "phrasal_verb",
                PartOfSpeechNameRu = Loc.Tr("Trainer_PhrasalVerb"),
                TranslationsRu = pv.Senses
                    .OrderBy(s => s.OrderIndex)
                    .SelectMany(s => s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu))
                    .Distinct()
                    .Take(6)
                    .ToList(),
                DefinitionRu = pv.Senses.OrderBy(s => s.OrderIndex).FirstOrDefault()?.DefinitionRu,
                DefinitionEn = pv.Senses.OrderBy(s => s.OrderIndex).FirstOrDefault()?.DefinitionEn,
                Examples = pv.Examples
                    .Where(e => !string.IsNullOrWhiteSpace(e.TextEn))
                    .Take(3)
                    .Select(e => new ExampleDetail { TextEn = e.TextEn, TextRu = e.TextRu, Source = e.Source })
                    .ToList(),
            };
        }
        if (p.Collocation is { } col)
        {
            return new TrainerCardViewModel
            {
                ProgressId = p.Id,
                Kind = TrainerCardKind.Collocation,
                OwnerId = col.Id,
                Headword = col.LinkedText,
                PartOfSpeechCode = "collocation",
                PartOfSpeechNameRu = Loc.Tr("Trainer_Collocation"),
                TranslationsRu = new[] { col.TranslationRu },
                DefinitionEn = col.DefinitionEn,
                Examples = string.IsNullOrWhiteSpace(col.ExampleEn)
                    ? Array.Empty<ExampleDetail>()
                    : new[] { new ExampleDetail { TextEn = col.ExampleEn, Source = "collocation" } },
            };
        }
        return null;
    }
}
