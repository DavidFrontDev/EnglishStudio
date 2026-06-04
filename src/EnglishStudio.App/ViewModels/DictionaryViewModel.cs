using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Content;
using EnglishStudio.Modules.Dictionary.Audio;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Dictionary.Data;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Images;
using EnglishStudio.Modules.Dictionary.Srs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AppPronCat = EnglishStudio.App.Audio.PronunciationCategory;
using EntityPronCat = EnglishStudio.Modules.Dictionary.Entities.PronunciationCategory;

namespace EnglishStudio.App.ViewModels;

public partial class DictionaryViewModel : ObservableObject
{
    private const int PageSize = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAudioCacheService _audioCache;
    private readonly IAudioPlayer _audioPlayer;
    private readonly IImageCacheService _imageCache;
    private readonly ISrsService _srs;
    private readonly IAudioRecorder _recorder;
    private readonly IWhisperTranscriber _whisper;
    private readonly PronunciationAssessor _assessor;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly DispatcherTimer _searchDebounce;
    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _detailCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private PosFilterItem? _selectedPos;

    [ObservableProperty]
    private SourceFilterItem? _selectedSource;

    [ObservableProperty]
    private CategoryFilterItem? _selectedCategory;

    [ObservableProperty]
    private TagFilterItem? _selectedTag;

    [ObservableProperty]
    private bool _hideStubs = true;

    [ObservableProperty]
    private int _totalWords;

    [ObservableProperty]
    private int _filteredCount;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True when Oxford 5000 (the dictionary's backbone) isn't imported — shows the gating banner.</summary>
    [ObservableProperty]
    private bool _isContentMissing;

    [ObservableProperty]
    private string _contentMissingText =
        "Словарь Oxford 5000 входит в контент-пак. Импортируйте пак, чтобы открыть словарь " +
        "(каркас AWL/AVL без Oxford малополезен).";

    [ObservableProperty]
    private WordListItem? _selectedWord;

    [ObservableProperty]
    private WordDetailViewModel? _detail;

    public ObservableCollection<WordListItem> Words { get; } = new();
    public ObservableCollection<CefrFilterItem> CefrFilters { get; }
    public ObservableCollection<PosFilterItem> PosOptions { get; } = new();
    public ObservableCollection<SourceFilterItem> SourceOptions { get; } = new();
    public ObservableCollection<CategoryFilterItem> CategoryOptions { get; } = new();
    public ObservableCollection<TagFilterItem> TagOptions { get; } = new();

    public DictionaryViewModel(
        IServiceScopeFactory scopeFactory,
        IAudioCacheService audioCache,
        IAudioPlayer audioPlayer,
        IImageCacheService imageCache,
        ISrsService srs,
        IAudioRecorder recorder,
        IWhisperTranscriber whisper,
        PronunciationAssessor assessor,
        IContentStore content,
        ContentImportLauncher importLauncher)
    {
        _scopeFactory = scopeFactory;
        _audioCache = audioCache;
        _audioPlayer = audioPlayer;
        _imageCache = imageCache;
        _srs = srs;
        _recorder = recorder;
        _whisper = whisper;
        _assessor = assessor;
        _content = content;
        _importLauncher = importLauncher;

        CefrFilters = new ObservableCollection<CefrFilterItem>(new[]
        {
            new CefrFilterItem { Level = CefrLevel.A1, Label = "A1" },
            new CefrFilterItem { Level = CefrLevel.A2, Label = "A2" },
            new CefrFilterItem { Level = CefrLevel.B1, Label = "B1" },
            new CefrFilterItem { Level = CefrLevel.B2, Label = "B2" },
            new CefrFilterItem { Level = CefrLevel.C1, Label = "C1" },
            new CefrFilterItem { Level = CefrLevel.C2, Label = "C2" },
        });
        foreach (var f in CefrFilters)
        {
            f.PropertyChanged += (_, _) => QueueReload();
        }

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ReloadAsync();
        };

        _ = InitializeAsync();
    }

    partial void OnSearchTextChanged(string value) => QueueReload();
    partial void OnSelectedPosChanged(PosFilterItem? value) => QueueReload();
    partial void OnSelectedSourceChanged(SourceFilterItem? value) => QueueReload();
    partial void OnSelectedCategoryChanged(CategoryFilterItem? value) => QueueReload();
    partial void OnSelectedTagChanged(TagFilterItem? value) => QueueReload();
    partial void OnHideStubsChanged(bool value) => QueueReload();

    partial void OnSelectedWordChanged(WordListItem? value)
    {
        _detailCts?.Cancel();
        // Also cancel any in-flight image fetch — otherwise a slow Wikipedia/Pexels response from
        // the previous word can land in CurrentImagePath after the user has moved on (or cleared
        // the selection entirely), leaving a stale image pinned to the panel.
        _imageCts?.Cancel();
        CurrentImagePath = null;
        ImageStatus = null;
        if (value is null)
        {
            Detail = null;
            return;
        }
        _detailCts = new CancellationTokenSource();
        _ = LoadDetailAsync(value.Id, _detailCts.Token);
    }

    private void QueueReload()
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task InitializeAsync()
    {
        // Oxford 5000 blocks the dictionary; PHaVE is partial content and does NOT block (see plan §B3).
        IsContentMissing = !_content.IsImported(ContentSection.DictionaryOxford);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        TotalWords = await db.Words.CountAsync();

        PosOptions.Clear();
        PosOptions.Add(new PosFilterItem { Code = null, Label = "Все части речи" });
        var partsOfSpeech = await db.PartsOfSpeech
            .OrderBy(p => p.Code)
            .Select(p => new { p.Code, p.NameRu })
            .ToListAsync();
        foreach (var p in partsOfSpeech)
        {
            PosOptions.Add(new PosFilterItem { Code = p.Code, Label = p.NameRu });
        }
        SelectedPos = PosOptions[0];

        SourceOptions.Clear();
        SourceOptions.Add(new SourceFilterItem { Source = null, Label = "Все источники" });
        SourceOptions.Add(new SourceFilterItem { Source = WordSource.Seed, Label = "Oxford 5000" });
        SourceOptions.Add(new SourceFilterItem { Source = WordSource.Awl,  Label = "AWL" });
        SourceOptions.Add(new SourceFilterItem { Source = WordSource.Avl,  Label = "AVL" });
        SourceOptions.Add(new SourceFilterItem { Source = WordSource.Phave, Label = "Фразовые глаголы (PHaVE)" });
        SourceOptions.Add(new SourceFilterItem { Source = WordSource.User, Label = "Мои слова" });
        SelectedSource = SourceOptions[0];

        CategoryOptions.Clear();
        CategoryOptions.Add(new CategoryFilterItem { Id = null, Label = "Все категории" });
        var cats = await db.Categories
            .OrderBy(c => c.OrderIndex)
            .Select(c => new { c.Id, c.Code, c.NameRu })
            .ToListAsync();
        foreach (var c in cats)
        {
            CategoryOptions.Add(new CategoryFilterItem { Id = c.Id, Code = c.Code, Label = c.NameRu });
        }
        SelectedCategory = CategoryOptions[0];

        TagOptions.Clear();
        TagOptions.Add(new TagFilterItem { Id = null, Label = "Все теги" });
        var tags = await db.Tags
            .Where(t => t.WordTags.Any())
            .OrderBy(t => t.Code)
            .Select(t => new { t.Id, t.Code, t.NameRu })
            .ToListAsync();
        foreach (var t in tags)
        {
            TagOptions.Add(new TagFilterItem { Id = t.Id, Code = t.Code, Label = t.NameRu });
        }
        SelectedTag = TagOptions[0];

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

            var levels = CefrFilters.Where(f => f.IsSelected).Select(f => f.Level).ToHashSet();

            var q = db.Words.AsQueryable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var pattern = SearchText.Trim().ToLowerInvariant();
                q = q.Where(w => EF.Functions.Like(w.Headword.ToLower(), pattern + "%")
                              || EF.Functions.Like(w.Lemma, pattern + "%"));
            }

            if (levels.Count > 0)
            {
                q = q.Where(w => levels.Contains(w.CefrLevel));
            }

            if (SelectedPos is { Code: { } posCode })
            {
                q = q.Where(w => w.PartOfSpeech.Code == posCode);
            }

            if (SelectedSource is { Source: { } src })
            {
                q = q.Where(w => w.Source == src);
            }

            if (SelectedCategory is { Id: { } catId })
            {
                q = q.Where(w => w.WordCategories.Any(wc => wc.CategoryId == catId));
            }

            if (SelectedTag is { Id: { } tagId })
            {
                q = q.Where(w => w.WordTags.Any(wt => wt.TagId == tagId));
            }

            if (HideStubs)
            {
                q = q.Where(w => w.Senses.Any());
            }

            FilteredCount = await q.CountAsync();

            var page = await q
                .OrderBy(w => w.Headword)
                .ThenBy(w => w.Id)
                .Take(PageSize)
                .Select(w => new WordListItem
                {
                    Id = w.Id,
                    Headword = w.Headword,
                    IpaUk = w.IpaUk,
                    Cefr = w.CefrLevel,
                    PosCode = w.PartOfSpeech.Code,
                })
                .ToListAsync();

            Words.Clear();
            foreach (var w in page) Words.Add(w);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadDetailAsync(int wordId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();

        var word = await db.Words
            .AsNoTracking()
            .Include(w => w.PartOfSpeech)
            .Include(w => w.Senses).ThenInclude(s => s.Translations)
            .Include(w => w.Examples)
            .FirstOrDefaultAsync(w => w.Id == wordId, ct);

        if (ct.IsCancellationRequested) return;
        if (word is null) { Detail = null; return; }

        Detail = new WordDetailViewModel
        {
            Id = word.Id,
            Headword = word.Headword,
            PartOfSpeechCode = word.PartOfSpeech.Code,
            PartOfSpeechNameRu = word.PartOfSpeech.NameRu,
            Cefr = word.CefrLevel,
            IpaUk = word.IpaUk,
            IpaUs = word.IpaUs,
            HasAudioUk = !string.IsNullOrWhiteSpace(word.AudioUkPath),
            HasAudioUs = !string.IsNullOrWhiteSpace(word.AudioUsPath),
            Senses = word.Senses
                .OrderBy(s => s.OrderIndex)
                .Select(s => new SenseDetail
                {
                    DefinitionEn = s.DefinitionEn,
                    DefinitionRu = s.DefinitionRu,
                    Translations = s.Translations.OrderBy(t => t.OrderIndex).Select(t => t.TextRu).ToList(),
                })
                .ToList(),
            Examples = word.Examples
                .Where(e => !string.IsNullOrWhiteSpace(e.TextEn))
                .GroupBy(e => e.TextEn)
                .Select(g => g.First())
                .Select(e => new ExampleDetail
                {
                    TextEn = e.TextEn,
                    TextRu = e.TextRu,
                    Source = e.Source,
                })
                .Take(10)
                .ToList(),
        };

        var isInTraining = await _srs.IsInTrainingForWordAsync(wordId);
        if (ct.IsCancellationRequested) return;
        Detail.IsInTraining = isInTraining;
        OnPropertyChanged(nameof(Detail));

        _ = StartImageLoadAsync(wordId);
    }

    [RelayCommand]
    private async Task AddToTrainingAsync()
    {
        var target = Detail;
        if (target is null || target.IsInTraining) return;
        await _srs.AddWordAsync(target.Id);
        // User may have switched words during the await — only mutate the snapshot we acted on.
        target.IsInTraining = true;
        if (ReferenceEquals(Detail, target)) OnPropertyChanged(nameof(Detail));
    }

    [ObservableProperty]
    private string? _currentImagePath;

    [ObservableProperty]
    private string? _imageStatus;

    private async Task StartImageLoadAsync(int wordId)
    {
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        var ct = _imageCts.Token;

        CurrentImagePath = null;
        ImageStatus = null;
        var progress = new Progress<string>(msg =>
        {
            if (ct.IsCancellationRequested) return;
            ImageStatus = string.IsNullOrEmpty(msg) ? null : msg;
        });
        try
        {
            var paths = await _imageCache.GetOrFetchAsync(wordId, maxImages: 1, progress, ct);
            if (ct.IsCancellationRequested) return;
            // Belt-and-braces: even if the token wasn't cancelled, the selection may have moved on
            // (we only cancel on the next SelectedWord change). Refuse to overwrite the image of a
            // different word.
            if (Detail?.Id != wordId) return;
            CurrentImagePath = paths.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch
        {
            // ignore — image is non-critical
        }
        finally
        {
            if (!ct.IsCancellationRequested) ImageStatus = null;
        }
    }

    [ObservableProperty]
    private bool _isAudioBusy;

    [ObservableProperty]
    private string? _audioStatus;

    [RelayCommand(CanExecute = nameof(CanPlayUk))]
    private Task PlayUkAsync() => PlayAudioAsync(AudioVariant.Uk);

    [RelayCommand(CanExecute = nameof(CanPlayUs))]
    private Task PlayUsAsync() => PlayAudioAsync(AudioVariant.Us);

    private bool CanPlayUk() => !IsAudioBusy && Detail is { HasAudioUk: true };
    private bool CanPlayUs() => !IsAudioBusy && Detail is { HasAudioUs: true };

    partial void OnDetailChanged(WordDetailViewModel? value)
    {
        PlayUkCommand.NotifyCanExecuteChanged();
        PlayUsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsAudioBusyChanged(bool value)
    {
        PlayUkCommand.NotifyCanExecuteChanged();
        PlayUsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var app = (App)System.Windows.Application.Current;
        var window = app.Services.GetRequiredService<Views.Settings.SettingsWindow>();
        window.DataContext = app.Services.GetRequiredService<SettingsViewModel>();
        window.Owner = app.MainWindow;
        window.ShowDialog();
    }

    /// <summary>Opens the content importer, then reloads (picks up freshly imported Oxford/PHaVE).</summary>
    [RelayCommand]
    private async Task OpenImportAsync()
    {
        _importLauncher.Show();
        await InitializeAsync();
    }

    [ObservableProperty]
    private bool _isPronunciationPanelOpen;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private string? _pronunciationStatus;

    [ObservableProperty]
    private string? _lastRecognizedText;

    [ObservableProperty]
    private int _lastScore;

    [ObservableProperty]
    private string? _lastFeedback;

    [ObservableProperty]
    private AppPronCat _lastCategory;

    [RelayCommand]
    private async Task TogglePronunciationPanelAsync()
    {
        IsPronunciationPanelOpen = !IsPronunciationPanelOpen;
        if (IsPronunciationPanelOpen && !_whisper.IsModelReady)
        {
            var progress = new Progress<string>(msg => PronunciationStatus = string.IsNullOrEmpty(msg) ? null : msg);
            await _whisper.EnsureModelDownloadedAsync(progress);
        }
    }

    private WordDetailViewModel? _recordingTarget;

    [RelayCommand]
    private async Task StartStopRecordingAsync()
    {
        if (Detail is null) return;

        if (_recorder.IsRecording)
        {
            var wav = _recorder.StopRecording();
            IsRecording = false;
            // Lock the target to whatever word was selected when recording started — the user
            // may switch words during the long transcription, but the score must attach to the
            // word that was actually spoken.
            var target = _recordingTarget;
            _recordingTarget = null;
            if (wav is null || target is null) return;

            IsTranscribing = true;
            PronunciationStatus = "Распознавание…";
            try
            {
                if (!_whisper.IsModelReady)
                {
                    var progress = new Progress<string>(msg => PronunciationStatus = string.IsNullOrEmpty(msg) ? null : msg);
                    var ok = await _whisper.EnsureModelDownloadedAsync(progress);
                    if (!ok)
                    {
                        PronunciationStatus = "Модель Whisper не загружена.";
                        return;
                    }
                }

                var text = await _whisper.TranscribeAsync(wav);
                var result = _assessor.Assess(target.Headword, text);
                // Only surface the score in the UI if the user is still looking at the same
                // word; otherwise just persist it silently against the correct WordId.
                if (ReferenceEquals(Detail, target))
                {
                    LastRecognizedText = string.IsNullOrWhiteSpace(result.Recognized) ? "(не распознано)" : result.Recognized;
                    LastScore = result.Score;
                    LastFeedback = result.FeedbackRu;
                    LastCategory = result.Category;
                }
                PronunciationStatus = null;

                // persist attempt
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DictionaryDbContext>();
                db.PronunciationAttempts.Add(new PronunciationAttempt
                {
                    WordId = target.Id,
                    TargetText = result.Target,
                    RecognizedText = result.Recognized,
                    Score = result.Score,
                    Category = result.Category switch
                    {
                        AppPronCat.Excellent => EntityPronCat.Excellent,
                        AppPronCat.Good => EntityPronCat.Good,
                        AppPronCat.Poor => EntityPronCat.Poor,
                        _ => EntityPronCat.Unrecognized,
                    },
                    RecordedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                PronunciationStatus = $"Ошибка: {ex.Message}";
            }
            finally
            {
                IsTranscribing = false;
            }
        }
        else
        {
            if (!_recorder.IsMicrophoneAvailable())
            {
                PronunciationStatus = "Микрофон не найден.";
                return;
            }
            LastRecognizedText = null;
            LastScore = 0;
            LastFeedback = null;
            PronunciationStatus = "Запись… нажмите ещё раз чтобы остановить.";
            _recordingTarget = Detail;
            _recorder.StartRecording();
            IsRecording = true;
        }
    }

    private async Task PlayAudioAsync(AudioVariant variant)
    {
        var wordId = Detail?.Id;
        if (wordId is null) return;

        IsAudioBusy = true;
        var ui = System.Threading.SynchronizationContext.Current;
        var progress = new Progress<string>(msg => AudioStatus = string.IsNullOrEmpty(msg) ? null : msg);
        try
        {
            var path = await _audioCache.GetOrFetchAsync(wordId.Value, variant, progress);
            if (!string.IsNullOrWhiteSpace(path))
            {
                _audioPlayer.Play(path);
            }
        }
        finally
        {
            IsAudioBusy = false;
            AudioStatus = null;
        }
    }
}
