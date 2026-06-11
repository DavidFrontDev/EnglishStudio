using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using EnglishStudio.App.Shell;
using EnglishStudio.App.ViewModels.ReadingStudy;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Services;

namespace EnglishStudio.App.Views.ReadingStudy;

/// <summary>
/// Standalone reading window (app chrome): palette header + the text on its chosen
/// background, with a selection→translation popup. Sizes to the text on open, then the
/// user can resize freely.
/// </summary>
public partial class ReaderWindow : ChromedWindow
{
    /// <summary>Fade factor for words behind the read-along cursor (transcription mode dims via opacity).</summary>
    private const double DimOpacity = 0.4;

    private readonly DispatcherTimer _debounce;
    private bool _built;

    // Read-along dimming + annotations: every word unit keyed by its global WordIndex, plus an
    // ordered list (document order) used for selection mapping, highlight painting and dimming.
    private readonly Dictionary<int, WordVisual> _wordByIndex = new();
    private readonly List<WordVisual> _words = new();
    private int _dimmedUpTo;
    private SolidColorBrush? _dimBrush;
    private ReaderViewModel? _subscribed;
    private ReadingAppearanceViewModel? _appearanceSub;

    // Latest annotation sets, repainted together (colour highlights under note highlights).
    private IReadOnlyList<NoteDto> _latestNotes = Array.Empty<NoteDto>();
    private IReadOnlyList<HighlightDto> _latestHighlights = Array.Empty<HighlightDto>();

    /// <summary>
    /// A single rendered word. In the default (fast) mode it is a flat <see cref="Run"/>; in
    /// transcription mode it is a vertical stack (IPA over word) wrapped in an
    /// <see cref="InlineUIContainer"/>. The accessor methods hide the difference so dimming,
    /// highlighting and selection mapping work identically in both modes.
    /// </summary>
    private sealed class WordVisual
    {
        public int WordIndex { get; init; }
        public int Start { get; init; }
        public int Len { get; init; }
        public string Text { get; init; } = string.Empty;

        // Exactly one rendering is populated.
        public Run? Run { get; init; }
        public InlineUIContainer? Container { get; init; }
        public TextBlock? WordBlock { get; init; }
        public Panel? Stack { get; init; }

        public TextPointer ContentStart => Run is not null ? Run.ContentStart : Container!.ContentStart;
        public TextPointer ContentEnd => Run is not null ? Run.ContentEnd : Container!.ContentEnd;

        public void Dim(Brush dimBrush)
        {
            if (Run is not null) Run.Foreground = dimBrush;
            else if (Stack is not null) Stack.Opacity = DimOpacity;
        }

        public void Undim()
        {
            // Run: restore inheritance from the RichTextBox (null would render invisible).
            if (Run is not null) Run.ClearValue(TextElement.ForegroundProperty);
            else Stack?.ClearValue(UIElement.OpacityProperty);
        }

        public void SetBackground(Brush brush)
        {
            if (Run is not null) Run.Background = brush;
            else if (WordBlock is not null) WordBlock.Background = brush;
        }

        public void ClearBackground()
        {
            if (Run is not null) Run.ClearValue(TextElement.BackgroundProperty);
            else WordBlock?.ClearValue(TextBlock.BackgroundProperty);
        }
    }

    public ReaderWindow()
    {
        InitializeComponent();

        // Fit-to-content is capped to the screen; overflow scrolls inside the reader.
        MaxWidth = SystemParameters.WorkArea.Width * 0.95;
        MaxHeight = SystemParameters.WorkArea.Height * 0.95;

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += OnDebounceTick;
    }

    private ReaderViewModel? Vm => DataContext as ReaderViewModel;

    private bool ShowTranscription => Vm?.Appearance.ShowTranscription == true;

    private void OnLoaded(object sender, RoutedEventArgs e) { }

    /// <summary>After the initial size-to-content pass, switch to manual so the user can resize.</summary>
    private void OnContentRendered(object? sender, EventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
    }

    private async void Reader_Loaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        _built = true;

        if (Vm is { } vm && !ReferenceEquals(_subscribed, vm))
        {
            _subscribed = vm;
            vm.CursorChanged += OnCursorChanged;
            vm.DimReset += OnDimReset;
            vm.NotesHighlightChanged += OnNotesHighlightChanged;
            vm.HighlightsChanged += OnHighlightsChanged;
            vm.ScrollToWordIndexRequested += OnScrollToWordIndex;
            vm.PageChanged += OnPageChanged;

            // Rebuild the document when the transcription toggle flips.
            _appearanceSub = vm.Appearance;
            _appearanceSub.PropertyChanged += OnAppearanceChanged;
        }

        // If transcription is already on (persisted within the session), warm the cache first
        // so the very first render shows the IPA rather than flashing plain text.
        if (ShowTranscription && Vm is { } v) await v.EnsureTranscriptionLoadedAsync();

        BuildDocument();
        // Apply any preloaded note highlights now that the runs exist.
        Vm?.ApplyInitialAnnotations();
    }

    private void OnAppearanceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReadingAppearanceViewModel.ShowTranscription))
            RebuildForTranscriptionToggle();
    }

    /// <summary>
    /// Re-renders the current page in/out of transcription mode without losing read-along
    /// progress or annotations (font size / colours rebind live and don't need a rebuild).
    /// </summary>
    private async void RebuildForTranscriptionToggle()
    {
        if (!_built) return;
        Vm?.ClosePopupCommand.Execute(null);

        // Turning transcription on: ensure the IPA cache is loaded before we render the stacks.
        if (ShowTranscription && Vm is { } vm) await vm.EnsureTranscriptionLoadedAsync();

        var cursor = _dimmedUpTo;
        BuildDocument();
        Vm?.ApplyInitialAnnotations();
        if (cursor > 0) OnCursorChanged(cursor); // re-dim words already read
    }

    /// <summary>Renders the current page as a FlowDocument: one Run per word (fast mode), or a
    /// stacked IPA-over-word unit per word when transcription is on.</summary>
    private void BuildDocument()
    {
        var vm = Vm;
        if (vm is null) return;

        _wordByIndex.Clear();
        _words.Clear();
        _dimmedUpTo = 0;

        var transcription = ShowTranscription;
        var appearance = vm.Appearance;

        // Font size / colour inherit from the RichTextBox (bound to Appearance) so they
        // update live without rebuilding the document. The transcription units bind explicitly.
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left
        };

        // F8: render only the current page's slice. Tokens keep their GLOBAL WordIndex / char
        // offsets, so dimming, notes and bookmarks map across pages exactly as before.
        var pageTokens = vm.PageTokens;
        var paragraphBreakThreshold = HasBlankLines(pageTokens) ? 2 : 1;

        var para = NewParagraph();
        var pendingBreaks = 0;

        foreach (var t in pageTokens)
        {
            if (t.Kind == TokenKind.Break)
            {
                pendingBreaks++;
                continue;
            }

            if (pendingBreaks > 0 && para.Inlines.Count > 0)
            {
                if (pendingBreaks >= paragraphBreakThreshold)
                {
                    doc.Blocks.Add(para);
                    para = NewParagraph();
                }
                else
                {
                    para.Inlines.Add(new Run(" "));
                }
            }
            pendingBreaks = 0;

            switch (t.Kind)
            {
                case TokenKind.Word:
                    var visual = transcription
                        ? BuildTranscriptionWord(t, appearance)
                        : BuildPlainWord(t);
                    para.Inlines.Add(visual.Run is { } r ? r : (Inline)visual.Container!);
                    if (t.WordIndex is int wi)
                    {
                        _wordByIndex[wi] = visual;
                        _words.Add(visual);
                    }
                    break;
                case TokenKind.Space:
                    if (para.Inlines.Count > 0)
                        para.Inlines.Add(new Run(" "));
                    break;
                default:
                    para.Inlines.Add(new Run(t.Text));
                    break;
            }
        }

        if (para.Inlines.Count > 0)
            doc.Blocks.Add(para);

        Reader.Document = doc;
    }

    private static WordVisual BuildPlainWord(TextToken t)
    {
        var run = new Run(t.Text) { Tag = t.WordIndex };
        return new WordVisual
        {
            WordIndex = t.WordIndex ?? -1,
            Start = t.StartOffset,
            Len = t.Length,
            Text = t.Text,
            Run = run
        };
    }

    /// <summary>A vertical stack — small IPA line over the word — wrapped so it flows/wraps inline.</summary>
    private WordVisual BuildTranscriptionWord(TextToken t, ReadingAppearanceViewModel appearance)
    {
        var ipa = Vm?.IpaFor(t.Text);

        var ipaBlock = new TextBlock
        {
            Text = ipa ?? string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Opacity = 0.7,                       // a touch muted, so the word stays primary
            Margin = new Thickness(0, 0, 0, 1)
        };
        ipaBlock.SetBinding(TextBlock.FontSizeProperty,
            new Binding(nameof(ReadingAppearanceViewModel.TranscriptionFontSize)) { Source = appearance });
        ipaBlock.SetBinding(TextBlock.ForegroundProperty,
            new Binding(nameof(ReadingAppearanceViewModel.TextBrush)) { Source = appearance });

        var wordBlock = new TextBlock
        {
            Text = t.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Tag = t.WordIndex
        };
        wordBlock.SetBinding(TextBlock.FontSizeProperty,
            new Binding(nameof(ReadingAppearanceViewModel.FontSize)) { Source = appearance });
        wordBlock.SetBinding(TextBlock.ForegroundProperty,
            new Binding(nameof(ReadingAppearanceViewModel.TextBrush)) { Source = appearance });

        var stack = new StackPanel { Orientation = Orientation.Vertical, Tag = t.WordIndex };
        stack.Children.Add(ipaBlock);
        stack.Children.Add(wordBlock);
        // A click selects the word so the existing select→translate / notes machinery applies
        // (drag-selection over InlineUIContainers is awkward, a click is reliable and natural).
        stack.MouseLeftButtonUp += OnWordStackClicked;

        var container = new InlineUIContainer(stack) { BaselineAlignment = BaselineAlignment.Bottom };

        return new WordVisual
        {
            WordIndex = t.WordIndex ?? -1,
            Start = t.StartOffset,
            Len = t.Length,
            Text = t.Text,
            Container = container,
            WordBlock = wordBlock,
            Stack = stack
        };
    }

    /// <summary>Transcription mode: a word click selects that word and triggers the lookup popup.</summary>
    private void OnWordStackClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int wi) return;
        if (!_wordByIndex.TryGetValue(wi, out var wv) || wv.Container is null) return;

        Reader.Selection.Select(wv.Container.ContentStart, wv.Container.ContentEnd);
        e.Handled = true; // keep our selection (don't let the RichTextBox collapse it to a caret)

        _debounce.Stop();
        _ = RunLookupAsync();
    }

    private static Paragraph NewParagraph() => new() { Margin = new Thickness(0, 0, 0, 10) };

    private static bool HasBlankLines(IReadOnlyList<TextToken> tokens)
    {
        var consecutive = 0;
        foreach (var t in tokens)
        {
            if (t.Kind == TokenKind.Break)
            {
                if (++consecutive >= 2) return true;
            }
            else if (t.Kind != TokenKind.Space)
            {
                consecutive = 0;
            }
        }
        return false;
    }

    private void Reader_SelectionChanged(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        await RunLookupAsync();
    }

    /// <summary>Resolves the current selection to a translation and positions/opens the popup.</summary>
    private async Task RunLookupAsync()
    {
        var vm = Vm;
        if (vm is null) return;

        var text = GetSelectionText();

        if (text.Length == 0 || text.Length > 300 || !ContainsLetter(text))
        {
            vm.ClosePopupCommand.Execute(null);
            return;
        }

        try
        {
            var rect = Reader.Selection.End.GetCharacterRect(LogicalDirection.Forward);
            if (!rect.IsEmpty)
            {
                TransPopup.HorizontalOffset = rect.X;
                TransPopup.VerticalOffset = rect.Bottom + 2;
            }
        }
        catch { /* selection rect unavailable — popup falls back to last offset */ }

        // Highlight is independent of the translation lookup — enable it for any real span and
        // reflect whether the span is already highlighted (drives the button's label).
        if (TryGetSelectionSpan(out var hs, out var hl, out _))
        {
            vm.Popup.CanHighlight = true;
            vm.Popup.IsSelectionHighlighted = vm.IsSpanHighlighted(hs, hl);
        }
        else
        {
            vm.Popup.CanHighlight = false;
            vm.Popup.IsSelectionHighlighted = false;
        }

        await vm.LookupSelectionAsync(text, GetContextSentence(Reader.Selection));
    }

    /// <summary>
    /// Selected text. In transcription mode the RichTextBox reports object-replacement chars for
    /// the per-word <see cref="InlineUIContainer"/>s, so the text is rebuilt from the word units
    /// that the selection overlaps (document order).
    /// </summary>
    private string GetSelectionText()
    {
        if (!ShowTranscription)
            return Reader.Selection.Text?.Trim() ?? string.Empty;

        var sel = Reader.Selection;
        var sb = new StringBuilder();
        foreach (var wv in _words)
        {
            if (sel.Start.CompareTo(wv.ContentEnd) < 0 && sel.End.CompareTo(wv.ContentStart) > 0)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(wv.Text);
            }
        }
        return sb.ToString().Trim();
    }

    private string? GetContextSentence(TextSelection sel)
    {
        var para = sel.Start.Paragraph;
        if (para is null) return null;

        string? txt;
        if (ShowTranscription)
        {
            // Rebuild the paragraph text from its word units (Selection.Text is object chars here).
            var sb = new StringBuilder();
            foreach (var wv in _words)
            {
                if (wv.ContentStart.CompareTo(para.ContentStart) >= 0 &&
                    wv.ContentEnd.CompareTo(para.ContentEnd) <= 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(wv.Text);
                }
            }
            txt = sb.ToString().Trim();
        }
        else
        {
            txt = new TextRange(para.ContentStart, para.ContentEnd).Text?.Trim();
        }

        if (string.IsNullOrEmpty(txt)) return null;
        return txt.Length > 400 ? txt[..400] : txt;
    }

    private static bool ContainsLetter(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c)) return true;
        return false;
    }

    // ── Read-along dimming ─────────────────────────────────────────────────

    /// <summary>Cursor moved forward (GLOBAL WordIndex): fade page words behind it. Iterates the
    /// current page's units (bounded ~page size) rather than a 0..cursor range, which would be huge
    /// for a page deep inside a book.</summary>
    private void OnCursorChanged(int cursor)
    {
        if (cursor <= _dimmedUpTo) return;
        _dimBrush ??= BuildDimBrush();

        foreach (var wv in _words)
            if (wv.WordIndex >= _dimmedUpTo && wv.WordIndex < cursor)
                wv.Dim(_dimBrush);

        _dimmedUpTo = cursor;
    }

    /// <summary>Page changed: rebuild the slice, re-apply note highlights, scroll to top.</summary>
    private void OnPageChanged()
    {
        BuildDocument();          // resets _wordByIndex/_words/_dimmedUpTo for the new slice
        Vm?.ApplyInitialAnnotations();
        FindContentScrollViewer()?.ScrollToVerticalOffset(0);
    }

    /// <summary>New session: restore full opacity and recapture the current text colour.</summary>
    private void OnDimReset()
    {
        foreach (var wv in _words)
            wv.Undim();

        _dimmedUpTo = 0;
        _dimBrush = BuildDimBrush();
    }

    /// <summary>The current text colour at ~40% alpha — a wash-out over the paper background.</summary>
    private SolidColorBrush BuildDimBrush()
    {
        var color = (Reader.Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
        var dim = new SolidColorBrush(Color.FromArgb((byte)(DimOpacity * 255), color.R, color.G, color.B));
        dim.Freeze();
        return dim;
    }

    // ── F5 notes: highlight, add-from-selection, bookmark scroll ────────────

    private void OnNotesHighlightChanged(IReadOnlyList<NoteDto> notes)
    {
        _latestNotes = notes;
        RepaintAnnotations();
    }

    private void OnHighlightsChanged(IReadOnlyList<HighlightDto> highlights)
    {
        _latestHighlights = highlights;
        RepaintAnnotations();
    }

    /// <summary>Clears all word backgrounds, then paints colour highlights and note highlights
    /// (notes on top, so a note's colour wins where they overlap).</summary>
    private void RepaintAnnotations()
    {
        foreach (var wv in _words)
            wv.ClearBackground();

        foreach (var h in _latestHighlights)
            PaintRange(h.StartOffset, h.Length, BuildHighlightBrush(h.Color));

        foreach (var note in _latestNotes)
            PaintRange(note.StartOffset, note.Length, BuildHighlightBrush(note.Color));
    }

    /// <summary>Tints every word unit whose char-range overlaps [start, start+length).</summary>
    private void PaintRange(int start, int length, SolidColorBrush brush)
    {
        var end = start + length;
        foreach (var wv in _words)
        {
            var wvEnd = wv.Start + wv.Len;
            if (wv.Start < end && wvEnd > start)
                wv.SetBackground(brush);
        }
    }

    private void OnScrollToWordIndex(int wordIndex)
    {
        if (!_wordByIndex.TryGetValue(wordIndex, out var wv)) return;
        var sv = FindContentScrollViewer();
        if (sv is null) return;

        try
        {
            var rect = wv.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + rect.Top - 40));
        }
        catch { /* layout not ready — ignore */ }
    }

    /// <summary>Popup "✎ Заметка": add a note for the current selection.</summary>
    private void AddNoteButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        if (!TryGetSelectionSpan(out var start, out var len, out var quote)) return;

        vm.ClosePopupCommand.Execute(null);
        vm.BeginAddNote(start, len, quote);
    }

    /// <summary>Popup "🖍 Выделить": toggle a purple highlight over the current selection.</summary>
    private async void HighlightButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        if (!TryGetSelectionSpan(out var start, out var len, out var quote)) return;

        await vm.ToggleHighlightAsync(start, len, quote);
        // Update the button label for the (still-selected) span.
        vm.Popup.IsSelectionHighlighted = vm.IsSpanHighlighted(start, len);
    }

    /// <summary>Toolbar "🔖": bookmark the top visible word.</summary>
    private async void BookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;
        var wi = TopVisibleWordIndex();
        if (wi is int idx) await vm.SetBookmarkAtAsync(idx);
    }

    private bool TryGetSelectionSpan(out int start, out int len, out string quote)
    {
        start = 0; len = 0; quote = string.Empty;

        var sel = Reader.Selection;
        var text = GetSelectionText();
        if (text.Length == 0) return false;

        var min = int.MaxValue;
        var max = int.MinValue;
        foreach (var wv in _words)
        {
            if (sel.Start.CompareTo(wv.ContentEnd) < 0 && sel.End.CompareTo(wv.ContentStart) > 0)
            {
                min = Math.Min(min, wv.Start);
                max = Math.Max(max, wv.Start + wv.Len);
            }
        }

        if (max <= min) return false;
        start = min;
        len = max - min;
        quote = text.Length > 200 ? text[..200] : text;
        return true;
    }

    private int? TopVisibleWordIndex()
    {
        foreach (var wv in _words)
        {
            try
            {
                var rect = wv.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (rect.Bottom > 0) return wv.WordIndex;
            }
            catch { /* skip */ }
        }
        return _words.Count > 0 ? _words[0].WordIndex : null;
    }

    private ScrollViewer? FindContentScrollViewer() =>
        Reader.Template?.FindName("PART_ContentHost", Reader) as ScrollViewer;

    private static SolidColorBrush BuildHighlightBrush(string? hex)
    {
        var color = Colors.Khaki;
        if (!string.IsNullOrWhiteSpace(hex))
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex)!; }
            catch { /* keep default */ }
        }
        var brush = new SolidColorBrush(Color.FromArgb(0x66, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        _debounce.Stop();

        if (_appearanceSub is { } ap)
        {
            ap.PropertyChanged -= OnAppearanceChanged;
            _appearanceSub = null;
        }

        if (_subscribed is { } vm)
        {
            vm.CursorChanged -= OnCursorChanged;
            vm.DimReset -= OnDimReset;
            vm.NotesHighlightChanged -= OnNotesHighlightChanged;
            vm.HighlightsChanged -= OnHighlightsChanged;
            vm.ScrollToWordIndexRequested -= OnScrollToWordIndex;
            vm.PageChanged -= OnPageChanged;
            vm.Shutdown();
            _subscribed = null;
        }
        base.OnClosed(e);
    }
}
