using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Dictionary.Srs;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Reader screen. Renders the text as per-word runs (built by the view) and resolves
/// mouse selections to translations via <see cref="ITextLookupService"/>. Phase 3/4 add
/// live read-along: a microphone-driven cursor (dimming + WPM via <see cref="IReadAlongController"/>),
/// then a post-read Whisper analysis, a saved session and a summary panel.
/// </summary>
public partial class ReaderViewModel : ObservableObject
{
    private readonly ITextLibraryService _library;
    private readonly ITextLookupService _lookup;
    private readonly IReadAlongController _controller;
    private readonly IReadingAnalysisService? _analysis;
    private readonly IReadingSessionService? _sessions;
    private readonly INotesService? _notesService;
    private readonly IPaginationService? _pagination;
    private readonly IReadingPracticeService _practice;
    private readonly IServiceProvider _services;
    private readonly ILogger<ReaderViewModel> _log;

    /// <summary>Default highlight colour (purple) for "🖍 Выделить".</summary>
    private const string HighlightColor = "#7C4DFF";

    private CancellationTokenSource? _lookupCts;
    private CancellationTokenSource _lifetimeCts = new();

    private int _textId;
    private int _totalWords;
    private DateTime _startedAt;
    private bool _sessionCompleted;
    private bool _shuttingDown;

    // F5 notes / bookmark, loaded with the text and applied once the document is built.
    private IReadOnlyList<NoteDto> _loadedNotes = Array.Empty<NoteDto>();
    private int? _bookmarkWordIndex;

    // Persistent colour highlights for this text (loaded with the text, repainted with notes).
    private IReadOnlyList<HighlightDto> _highlights = Array.Empty<HighlightDto>();

    // F8 pagination: pages cover the whole text in GLOBAL coords; only the current page is rendered.
    private IReadOnlyList<TextPage> _pages = Array.Empty<TextPage>();
    private TextPage? _currentPage;
    private int _pageWordCount;

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private CefrLevel _cefr = CefrLevel.Unknown;
    [ObservableProperty] private int _wordCount;
    [ObservableProperty] private bool _isPopupOpen;

    // ── Live read-along state ──────────────────────────────────────────────
    [ObservableProperty] private ReadAlongState _readState = ReadAlongState.Idle;
    [ObservableProperty] private bool _isReading;
    [ObservableProperty] private bool _isModelLoading;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private double _currentWpm;
    [ObservableProperty] private int _wordsRead;
    [ObservableProperty] private double _elapsedSec;
    [ObservableProperty] private double _progressFraction;
    [ObservableProperty] private string? _modelStatus;

    [ObservableProperty] private ReadingSummaryViewModel? _summary;
    [ObservableProperty] private bool _isSummaryVisible;

    // ── F1 pre-teach / F2 comprehension overlays ───────────────────────────
    [ObservableProperty] private PreTeachViewModel? _preTeach;
    [ObservableProperty] private bool _isPreTeachVisible;
    [ObservableProperty] private ComprehensionViewModel? _comprehension;
    [ObservableProperty] private bool _isComprehensionVisible;

    // ── F4 shadowing overlay ───────────────────────────────────────────────
    [ObservableProperty] private ShadowingViewModel? _shadowing;
    [ObservableProperty] private bool _isShadowingVisible;

    // ── F5 notes overlay / bookmark ────────────────────────────────────────
    [ObservableProperty] private NotesPanelViewModel? _notesPanel;
    [ObservableProperty] private bool _isNotesVisible;
    [ObservableProperty] private bool _hasBookmark;

    // ── F8 pagination ──────────────────────────────────────────────────────
    [ObservableProperty] private int _currentPageIndex;
    [ObservableProperty] private bool _hasMultiplePages;
    [ObservableProperty] private string _pageLabel = string.Empty;

    /// <summary>Pager chrome shows only for multi-page texts when not actively reading aloud.</summary>
    public bool ShowPager => HasMultiplePages && !IsReading;
    public bool CanPrevPage => CurrentPageIndex > 0;
    public bool CanNextPage => CurrentPageIndex < _pages.Count - 1;

    partial void OnIsReadingChanged(bool value) => OnPropertyChanged(nameof(ShowPager));
    partial void OnHasMultiplePagesChanged(bool value) => OnPropertyChanged(nameof(ShowPager));

    partial void OnCurrentPageIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CanPrevPage));
        OnPropertyChanged(nameof(CanNextPage));
    }

    /// <summary>Tokens of the CURRENT page only (global WordIndex/offset preserved). Rendered by the view.</summary>
    public IReadOnlyList<TextToken> PageTokens { get; private set; } = Array.Empty<TextToken>();

    /// <summary>Raised when the current page changes so the view rebuilds the document slice.</summary>
    public event Action? PageChanged;

    /// <summary>"m:ss" view of <see cref="ElapsedSec"/> for the HUD.</summary>
    public string ElapsedText
    {
        get
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, ElapsedSec));
            return ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
        }
    }

    /// <summary>Tokens for the view to render as per-word runs. Set by <see cref="LoadAsync"/>.</summary>
    public IReadOnlyList<TextToken> Tokens { get; private set; } = Array.Empty<TextToken>();

    public LookupPopupViewModel Popup { get; }

    /// <summary>Shared reading-surface appearance (background, text colour, font size).</summary>
    public ReadingAppearanceViewModel Appearance { get; }

    /// <summary>Raised on every cursor move so the view can dim words before the cursor.</summary>
    public event Action<int>? CursorChanged;

    /// <summary>Raised when a new session starts so the view can clear all dimming.</summary>
    public event Action? DimReset;

    /// <summary>Raised when the set of notes changes so the view can re-highlight runs (F5).</summary>
    public event Action<IReadOnlyList<NoteDto>>? NotesHighlightChanged;

    /// <summary>Raised when the set of colour highlights changes so the view can re-paint them.</summary>
    public event Action<IReadOnlyList<HighlightDto>>? HighlightsChanged;

    /// <summary>Raised to ask the view to scroll a given word index into view (bookmark / note nav).</summary>
    public event Action<int>? ScrollToWordIndexRequested;

    /// <summary>Raised when the user wants to return to the library.</summary>
    public event Action? CloseRequested;

    public ReaderViewModel(
        ITextLibraryService library,
        ITextLookupService lookup,
        ISrsService srs,
        ReadingAppearanceViewModel appearance,
        IReadAlongController controller,
        IServiceProvider services,
        ILogger<ReaderViewModel> log)
    {
        _library = library;
        _lookup = lookup;
        _controller = controller;
        _services = services;
        _log = log;
        Appearance = appearance;
        _practice = services.GetRequiredService<IReadingPracticeService>();
        Popup = new LookupPopupViewModel(srs, _practice, log);

        // Analysis + persistence come from Agent A's AddReadingEngine(). They may be absent
        // while the UI is developed against the Fake controller — resolve optionally so the
        // reader still runs (summary then shows speed/coverage only, no save/history).
        _analysis = services.GetService<IReadingAnalysisService>();
        _sessions = services.GetService<IReadingSessionService>();
        _notesService = services.GetService<INotesService>();
        _pagination = services.GetService<IPaginationService>();

        _controller.StateChanged += OnStateChanged;
        _controller.ModelDownloadStatus += OnModelDownloadStatus;
        _controller.Progress += OnProgress;
        _controller.Finished += OnFinished;
    }

    partial void OnElapsedSecChanged(double value) => OnPropertyChanged(nameof(ElapsedText));

    public async Task LoadAsync(int textId)
    {
        var detail = await _library.GetAsync(textId);
        if (detail is null) return;

        _textId = textId;
        Title = detail.Title;
        Cefr = detail.EstimatedCefr;
        WordCount = detail.WordCount;
        Tokens = ReadingTokenizer.Tokenize(detail.BodyText);
        _totalWords = CountWords(Tokens);
        Popup.SetContext(textId);

        await _library.TouchOpenedAsync(textId);

        // Persistent colour highlights — applied with notes once the document is built.
        try { _highlights = await _practice.ListHighlightsAsync(textId); }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to load highlights for text {TextId}", textId); }

        // F5: preload notes + bookmark; the view applies them once the document is built.
        if (_notesService is not null)
        {
            try
            {
                _loadedNotes = await _notesService.ListNotesAsync(textId);
                var bookmark = await _notesService.GetBookmarkAsync(textId);
                _bookmarkWordIndex = bookmark?.WordIndex;
                HasBookmark = _bookmarkWordIndex is not null;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to load notes/bookmark for text {TextId}", textId);
            }
        }

        // F8: paginate; start on the bookmark's page if there is one.
        _pages = _pagination?.Paginate(Tokens, null) ?? SingleWholePage();
        HasMultiplePages = _pages.Count > 1;

        var startPage = 0;
        if (_bookmarkWordIndex is int bm)
        {
            var p = PageIndexOfWord(bm);
            if (p >= 0) startPage = p;
        }
        SetPage(startPage, raisePageChanged: false);
    }

    /// <summary>Called by the view after the document is built, to apply note + colour highlights.</summary>
    public void ApplyInitialAnnotations()
    {
        NotesHighlightChanged?.Invoke(_loadedNotes);
        HighlightsChanged?.Invoke(_highlights);
    }

    /// <summary>
    /// Toggles a purple highlight over the current selection: removes any highlight intersecting the
    /// span, or adds one if none did. Called from the popup's "🖍 Выделить" button.
    /// </summary>
    public async Task ToggleHighlightAsync(int startOffset, int length, string quote)
    {
        if (length <= 0) return;
        try
        {
            var removed = await _practice.RemoveHighlightsOverlappingAsync(_textId, startOffset, length, _lifetimeCts.Token);
            if (removed == 0)
                await _practice.AddHighlightAsync(_textId, startOffset, length, quote, HighlightColor, _lifetimeCts.Token);

            _highlights = await _practice.ListHighlightsAsync(_textId, _lifetimeCts.Token);
            HighlightsChanged?.Invoke(_highlights);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to toggle highlight for text {TextId}", _textId);
        }
    }

    /// <summary>True if any current highlight intersects [startOffset, startOffset+length).</summary>
    public bool IsSpanHighlighted(int startOffset, int length)
    {
        var end = startOffset + length;
        foreach (var h in _highlights)
            if (h.StartOffset < end && h.StartOffset + h.Length > startOffset) return true;
        return false;
    }

    // ── F8 pagination ──────────────────────────────────────────────────────

    [RelayCommand]
    private void NextPage()
    {
        if (IsReading || !CanNextPage) return;
        SetPage(CurrentPageIndex + 1, raisePageChanged: true);
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (IsReading || !CanPrevPage) return;
        SetPage(CurrentPageIndex - 1, raisePageChanged: true);
    }

    public void GoToPage(int index)
    {
        if (IsReading) return;
        SetPage(index, raisePageChanged: true);
    }

    private void SetPage(int index, bool raisePageChanged)
    {
        if (_pages.Count == 0) { PageTokens = Tokens; _pageWordCount = _totalWords; return; }

        index = Math.Clamp(index, 0, _pages.Count - 1);
        CurrentPageIndex = index;
        _currentPage = _pages[index];

        BuildPageSlice();

        PageLabel = !string.IsNullOrWhiteSpace(_currentPage.Heading)
            ? _currentPage.Heading!
            : $"Стр. {index + 1} из {_pages.Count}";

        if (raisePageChanged) PageChanged?.Invoke();
    }

    /// <summary>Slices <see cref="Tokens"/> to the current page by global char offsets (end is exclusive).</summary>
    private void BuildPageSlice()
    {
        var page = _currentPage;
        if (page is null) { PageTokens = Tokens; _pageWordCount = _totalWords; return; }

        var slice = new List<TextToken>();
        var wc = 0;
        foreach (var t in Tokens)
        {
            if (t.StartOffset < page.StartCharOffset || t.StartOffset >= page.EndCharOffset) continue;
            slice.Add(t);
            if (t.Kind == TokenKind.Word) wc++;
        }
        PageTokens = slice;
        _pageWordCount = wc;
    }

    private int PageIndexOfWord(int wordIndex)
    {
        if (_pages.Count == 0) return -1;
        if (_pagination is not null) return _pagination.PageOfWord(_pages, wordIndex);
        for (var i = 0; i < _pages.Count; i++)
            if (wordIndex >= _pages[i].StartWordIndex && wordIndex <= _pages[i].EndWordIndex) return i;
        return -1;
    }

    private IReadOnlyList<TextPage> SingleWholePage()
    {
        var firstWi = 0;
        var lastWi = Math.Max(0, _totalWords - 1);
        return new[] { new TextPage(0, firstWi, lastWi, 0, int.MaxValue, null) };
    }

    // ── Read-along commands ────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartReading()
    {
        if (IsReading || Tokens.Count == 0) return;

        // Reset visuals for a fresh attempt.
        IsSummaryVisible = false;
        Summary = null;
        CurrentWpm = 0;
        WordsRead = 0;
        ElapsedSec = 0;
        ProgressFraction = 0;
        _sessionCompleted = false;
        DimReset?.Invoke();

        var ct = _lifetimeCts.Token;

        if (!_controller.IsModelReady)
        {
            IsModelLoading = true;
            ModelStatus = "Готовлю распознаватель речи…";
            try
            {
                var progress = new Progress<string>(s => ModelStatus = s);
                var ok = await _controller.EnsureModelAsync(progress, ct);
                if (!ok)
                {
                    ModelStatus = "Не удалось загрузить распознаватель.";
                    return;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to prepare the speech model");
                ModelStatus = "Не удалось загрузить распознаватель.";
                return;
            }
            finally
            {
                IsModelLoading = false;
            }
            ModelStatus = null;
        }

        _startedAt = DateTime.Now;
        IsReading = true;
        try
        {
            // Read-along runs on the CURRENT page only (reading a whole book aloud is unhelpful).
            await _controller.StartAsync(PageTokens, ct);
        }
        catch (OperationCanceledException)
        {
            IsReading = false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start read-along");
            IsReading = false;
            ReadState = ReadAlongState.Error;
        }
    }

    [RelayCommand]
    private async Task StopReading()
    {
        if (ReadState != ReadAlongState.Listening && !IsReading) return;
        try
        {
            await _controller.StopAsync();
            // Completion (analysis → save → summary) is handled in OnFinished, which the
            // controller raises both on Stop and at end of text.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to stop read-along");
        }
    }

    // ── Controller events (raised on the UI thread per the contract) ───────

    private void OnStateChanged(object? sender, ReadAlongState state)
    {
        ReadState = state;
        IsModelLoading = state == ReadAlongState.LoadingModel;
        if (state is ReadAlongState.Idle or ReadAlongState.Finished or ReadAlongState.Error)
            IsReading = false;
        else if (state == ReadAlongState.Listening)
            IsReading = true;
    }

    private void OnModelDownloadStatus(object? sender, string status) => ModelStatus = status;

    private void OnProgress(object? sender, ReadAlongProgress p)
    {
        CurrentWpm = p.Wpm;
        WordsRead = p.WordsRead;
        ElapsedSec = p.ElapsedSec;

        // The controller's cursor is 0-based over the words we passed it (the PAGE slice), i.e.
        // page-local. Map it to a GLOBAL WordIndex so dimming lines up with the rendered runs.
        ProgressFraction = _pageWordCount > 0
            ? Math.Clamp((double)p.CursorWordIndex / _pageWordCount, 0, 1)
            : 0;
        var pageStart = _currentPage?.StartWordIndex ?? 0;
        CursorChanged?.Invoke(pageStart + p.CursorWordIndex);
    }

    private async void OnFinished(object? sender, ReadingRunResult result)
    {
        if (_sessionCompleted || _shuttingDown) return;
        _sessionCompleted = true;
        IsReading = false;

        var ct = _lifetimeCts.Token;

        ReadingAnalysis? analysis = null;
        if (_analysis is not null && !string.IsNullOrEmpty(result.WavPath))
        {
            try
            {
                IsAnalyzing = true;
                // Analyse against the page slice that was read (outcomes carry global WordIndex).
                analysis = await _analysis.AnalyzeAsync(result.WavPath!, PageTokens, null, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Reading analysis failed");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        var accuracy = analysis?.AccuracyPct ?? 0;
        var durationSec = (int)Math.Round(result.ElapsedSec);

        if (_sessions is not null)
        {
            try
            {
                await _sessions.SaveAsync(
                    _textId, _startedAt, durationSec, result.WordsRead,
                    result.Wpm, accuracy, result.Completed, result.WavPath,
                    analysis?.Words, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Saving reading session failed");
            }
        }

        IReadOnlyList<ReadingSessionSummary> history = Array.Empty<ReadingSessionSummary>();
        if (_sessions is not null)
        {
            try { history = await _sessions.ListByTextAsync(_textId, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Loading reading history failed"); }
        }

        if (_shuttingDown) return;

        try
        {
            var summary = _services.GetService<ReadingSummaryViewModel>();
            if (summary is not null)
            {
                summary.CloseRequested -= OnSummaryClosed;
                summary.CloseRequested += OnSummaryClosed;
                summary.QuestionsRequested -= OnSummaryQuestions;
                summary.QuestionsRequested += OnSummaryQuestions;
                await summary.InitializeAsync(_textId, Title, result, analysis, Tokens, history);
                Summary = summary;
                IsSummaryVisible = true;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to build the reading summary");
        }
    }

    private void OnSummaryClosed() => IsSummaryVisible = false;

    private void OnSummaryQuestions()
    {
        IsSummaryVisible = false;
        _ = ShowComprehensionCommand.ExecuteAsync(null);
    }

    // ── F1 pre-teach / F2 comprehension ────────────────────────────────────

    /// <summary>Opens the pre-teach panel (also invokable from the library "Подготовка" button).</summary>
    [RelayCommand]
    private async Task ShowPreTeach()
    {
        var vm = _services.GetService<PreTeachViewModel>();
        if (vm is null) return;

        vm.CloseRequested -= OnPreTeachClosed;
        vm.CloseRequested += OnPreTeachClosed;
        PreTeach = vm;
        IsPreTeachVisible = true;
        try { await vm.InitializeAsync(_textId, _lifetimeCts.Token); }
        catch (OperationCanceledException) { }
    }

    private void OnPreTeachClosed() => IsPreTeachVisible = false;

    /// <summary>Opens the comprehension-questions panel.</summary>
    [RelayCommand]
    private async Task ShowComprehension()
    {
        var vm = _services.GetService<ComprehensionViewModel>();
        if (vm is null) return;

        vm.CloseRequested -= OnComprehensionClosed;
        vm.CloseRequested += OnComprehensionClosed;
        Comprehension = vm;
        IsComprehensionVisible = true;
        try { await vm.InitializeAsync(_textId, _lifetimeCts.Token); }
        catch (OperationCanceledException) { }
    }

    private void OnComprehensionClosed() => IsComprehensionVisible = false;

    /// <summary>Opens the shadowing (pronunciation training) panel.</summary>
    [RelayCommand]
    private async Task ShowShadowing()
    {
        var vm = _services.GetService<ShadowingViewModel>();
        if (vm is null) return;

        vm.CloseRequested -= OnShadowingClosed;
        vm.CloseRequested += OnShadowingClosed;
        Shadowing = vm;
        IsShadowingVisible = true;
        // Shadowing covers the current page's sentences (not the whole book).
        try { await vm.InitializeAsync(PageTokens, _lifetimeCts.Token); }
        catch (OperationCanceledException) { }
    }

    private void OnShadowingClosed() => IsShadowingVisible = false;

    // ── F5 notes / bookmark ────────────────────────────────────────────────

    /// <summary>Opens the notes panel.</summary>
    [RelayCommand]
    private async Task ShowNotes()
    {
        await EnsureNotesPanelAsync();
        IsNotesVisible = true;
    }

    /// <summary>Opens the notes panel pre-filled to add a note for the selected span (called by the view).</summary>
    public async void BeginAddNote(int startOffset, int length, string quote)
    {
        if (length <= 0) return;
        await EnsureNotesPanelAsync();
        NotesPanel?.BeginAdd(startOffset, length, quote);
        IsNotesVisible = true;
    }

    private async Task EnsureNotesPanelAsync()
    {
        if (NotesPanel is not null) return;

        var vm = _services.GetService<NotesPanelViewModel>();
        if (vm is null) return;

        vm.CloseRequested -= OnNotesClosed;
        vm.CloseRequested += OnNotesClosed;
        vm.NotesChanged -= OnNotesChanged;
        vm.NotesChanged += OnNotesChanged;
        vm.NavigateRequested -= OnNoteNavigate;
        vm.NavigateRequested += OnNoteNavigate;

        NotesPanel = vm;
        try { await vm.InitializeAsync(_textId, _lifetimeCts.Token); }
        catch (OperationCanceledException) { }
    }

    private void OnNotesClosed() => IsNotesVisible = false;

    private async void OnNotesChanged()
    {
        // Re-pull notes and re-highlight the document.
        if (_notesService is null) return;
        try
        {
            _loadedNotes = await _notesService.ListNotesAsync(_textId, _lifetimeCts.Token);
            NotesHighlightChanged?.Invoke(_loadedNotes);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to refresh note highlights"); }
    }

    private void OnNoteNavigate(NoteDto note)
    {
        IsNotesVisible = false;
        var wi = WordIndexAtOffset(note.StartOffset);
        if (wi is int idx)
        {
            EnsurePageForWord(idx);
            ScrollToWordIndexRequested?.Invoke(idx);
        }
    }

    /// <summary>Persists the bookmark at <paramref name="wordIndex"/> (called by the view).</summary>
    public async Task SetBookmarkAtAsync(int wordIndex)
    {
        if (_notesService is null) return;
        try
        {
            await _notesService.SetBookmarkAsync(_textId, wordIndex, _lifetimeCts.Token);
            _bookmarkWordIndex = wordIndex;
            HasBookmark = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to set bookmark"); }
    }

    /// <summary>Opens the bookmark's page and scrolls to it (F5 "continue" = F8 page of the bookmark).</summary>
    [RelayCommand]
    private void ContinueFromBookmark()
    {
        if (_bookmarkWordIndex is int idx)
        {
            EnsurePageForWord(idx);
            ScrollToWordIndexRequested?.Invoke(idx);
        }
    }

    /// <summary>Switches to the page that contains <paramref name="wordIndex"/> (if not already shown).</summary>
    private void EnsurePageForWord(int wordIndex)
    {
        var page = PageIndexOfWord(wordIndex);
        if (page >= 0 && page != CurrentPageIndex) SetPage(page, raisePageChanged: true);
    }

    /// <summary>Maps a character offset in BodyText to the word token's WordIndex at/after it.</summary>
    private int? WordIndexAtOffset(int offset)
    {
        int? best = null;
        foreach (var t in Tokens)
        {
            if (t.Kind != TokenKind.Word || t.WordIndex is not int wi) continue;
            if (offset >= t.StartOffset && offset < t.StartOffset + t.Length) return wi;
            if (t.StartOffset >= offset) { best ??= wi; }
        }
        return best;
    }

    // ── Translation popup ──────────────────────────────────────────────────

    /// <summary>Called by the view when the selection settles. Looks up and shows the popup.</summary>
    public async Task LookupSelectionAsync(string selectedText, string? contextSentence)
    {
        _lookupCts?.Cancel();
        _lookupCts = new CancellationTokenSource();
        var ct = _lookupCts.Token;

        IsPopupOpen = true;
        Popup.BeginLookup(selectedText.Trim());

        try
        {
            var result = await _lookup.LookupAsync(selectedText, contextSentence, ct);
            if (ct.IsCancellationRequested) return;
            await Popup.ApplyAsync(result, _lookup.CanEnrich);
        }
        catch (OperationCanceledException) { /* superseded by a newer selection */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Lookup failed for '{Text}'", selectedText);
            if (!ct.IsCancellationRequested)
                await Popup.ApplyAsync(WordLookupResult.NotFound(selectedText.Trim()), _lookup.CanEnrich);
        }
    }

    [RelayCommand]
    private void ClosePopup()
    {
        _lookupCts?.Cancel();
        IsPopupOpen = false;
    }

    [RelayCommand]
    private void Close()
    {
        _lookupCts?.Cancel();
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Releases the microphone and detaches controller events when the reader window closes.
    /// Detaching first means a late <see cref="IReadAlongController.Finished"/> can't try to
    /// save a session against a torn-down window.
    /// </summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        _lifetimeCts.Cancel();

        _controller.StateChanged -= OnStateChanged;
        _controller.ModelDownloadStatus -= OnModelDownloadStatus;
        _controller.Progress -= OnProgress;
        _controller.Finished -= OnFinished;

        if (ReadState is ReadAlongState.Listening or ReadAlongState.LoadingModel)
        {
            // Fire-and-forget: release the capture device; result is discarded on close.
            _ = _controller.StopAsync();
        }

        // Release the shadowing capture device / stop TTS if that overlay was open.
        Shadowing?.Cleanup();

        _lookupCts?.Cancel();
    }

    private static int CountWords(IReadOnlyList<TextToken> tokens)
    {
        var n = 0;
        foreach (var t in tokens)
            if (t.Kind == TokenKind.Word) n++;
        return n;
    }
}
