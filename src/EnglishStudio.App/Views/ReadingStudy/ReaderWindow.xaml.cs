using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private readonly DispatcherTimer _debounce;
    private bool _built;

    // Read-along dimming: every word Run keyed by its WordIndex, the cursor reached so far,
    // and a cached semi-transparent brush used to fade words behind the cursor.
    private readonly Dictionary<int, Run> _wordRuns = new();
    private int _dimmedUpTo;
    private SolidColorBrush? _dimBrush;
    private ReaderViewModel? _subscribed;

    // F5: word runs with their BodyText char range, for note highlighting / selection→offset.
    private readonly List<WordSpan> _wordSpans = new();

    // Latest annotation sets, repainted together (colour highlights under note highlights).
    private IReadOnlyList<NoteDto> _latestNotes = Array.Empty<NoteDto>();
    private IReadOnlyList<HighlightDto> _latestHighlights = Array.Empty<HighlightDto>();

    private readonly record struct WordSpan(int Start, int Len, int WordIndex, Run Run);

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

    private void OnLoaded(object sender, RoutedEventArgs e) { }

    /// <summary>After the initial size-to-content pass, switch to manual so the user can resize.</summary>
    private void OnContentRendered(object? sender, EventArgs e)
    {
        SizeToContent = SizeToContent.Manual;
    }

    private void Reader_Loaded(object sender, RoutedEventArgs e)
    {
        if (_built) return;
        BuildDocument();
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
        }

        // Apply any preloaded note highlights now that the runs exist.
        Vm?.ApplyInitialAnnotations();
    }

    /// <summary>Renders the tokens as a FlowDocument, one Run per word (Tag = word index for Phase 3 dimming).</summary>
    private void BuildDocument()
    {
        var vm = Vm;
        if (vm is null) return;

        _wordRuns.Clear();
        _wordSpans.Clear();
        _dimmedUpTo = 0;

        // Font size / colour inherit from the RichTextBox (bound to Appearance) so they
        // update live without rebuilding the document.
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
                    var wordRun = new Run(t.Text) { Tag = t.WordIndex };
                    para.Inlines.Add(wordRun);
                    if (t.WordIndex is int wi)
                    {
                        _wordRuns[wi] = wordRun;
                        _wordSpans.Add(new WordSpan(t.StartOffset, t.Length, wi, wordRun));
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
        var vm = Vm;
        if (vm is null) return;

        var text = Reader.Selection.Text?.Trim() ?? string.Empty;

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

    private static string? GetContextSentence(TextSelection sel)
    {
        var para = sel.Start.Paragraph;
        if (para is null) return null;

        var txt = new TextRange(para.ContentStart, para.ContentEnd).Text?.Trim();
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
    /// current page's spans (bounded ~page size) rather than a 0..cursor range, which would be huge
    /// for a page deep inside a book.</summary>
    private void OnCursorChanged(int cursor)
    {
        if (cursor <= _dimmedUpTo) return;
        _dimBrush ??= BuildDimBrush();

        foreach (var span in _wordSpans)
            if (span.WordIndex >= _dimmedUpTo && span.WordIndex < cursor)
                span.Run.Foreground = _dimBrush;

        _dimmedUpTo = cursor;
    }

    /// <summary>Page changed: rebuild the slice, re-apply note highlights, scroll to top.</summary>
    private void OnPageChanged()
    {
        BuildDocument();          // resets _wordRuns/_wordSpans/_dimmedUpTo for the new slice
        Vm?.ApplyInitialAnnotations();
        FindContentScrollViewer()?.ScrollToVerticalOffset(0);
    }

    /// <summary>New session: restore full opacity and recapture the current text colour.</summary>
    private void OnDimReset()
    {
        foreach (var run in _wordRuns.Values)
            run.ClearValue(TextElement.ForegroundProperty); // restore inheritance from the RichTextBox (null would render invisible)

        _dimmedUpTo = 0;
        _dimBrush = BuildDimBrush();
    }

    /// <summary>The current text colour at ~40% alpha — a wash-out over the paper background.</summary>
    private SolidColorBrush BuildDimBrush()
    {
        var color = (Reader.Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
        var dim = new SolidColorBrush(Color.FromArgb((byte)(0.4 * 255), color.R, color.G, color.B));
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
        foreach (var span in _wordSpans)
            span.Run.ClearValue(TextElement.BackgroundProperty);

        foreach (var h in _latestHighlights)
            PaintRange(h.StartOffset, h.Length, BuildHighlightBrush(h.Color));

        foreach (var note in _latestNotes)
            PaintRange(note.StartOffset, note.Length, BuildHighlightBrush(note.Color));
    }

    /// <summary>Tints every word run whose char-range overlaps [start, start+length).</summary>
    private void PaintRange(int start, int length, SolidColorBrush brush)
    {
        var end = start + length;
        foreach (var span in _wordSpans)
        {
            var spanEnd = span.Start + span.Len;
            if (span.Start < end && spanEnd > start)
                span.Run.Background = brush;
        }
    }

    private void OnScrollToWordIndex(int wordIndex)
    {
        if (!_wordRuns.TryGetValue(wordIndex, out var run)) return;
        var sv = FindContentScrollViewer();
        if (sv is null) return;

        try
        {
            var rect = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
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
        var text = sel.Text?.Trim() ?? string.Empty;
        if (text.Length == 0) return false;

        var min = int.MaxValue;
        var max = int.MinValue;
        foreach (var span in _wordSpans)
        {
            if (sel.Start.CompareTo(span.Run.ContentEnd) < 0 && sel.End.CompareTo(span.Run.ContentStart) > 0)
            {
                min = Math.Min(min, span.Start);
                max = Math.Max(max, span.Start + span.Len);
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
        foreach (var span in _wordSpans)
        {
            try
            {
                var rect = span.Run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                if (rect.Bottom > 0) return span.WordIndex;
            }
            catch { /* skip */ }
        }
        return _wordSpans.Count > 0 ? _wordSpans[0].WordIndex : null;
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
