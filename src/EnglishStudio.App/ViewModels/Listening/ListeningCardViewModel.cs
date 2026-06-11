using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Reading.Questions;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Listening;

/// <summary>
/// One Listening "card" = one <see cref="TestQuestionGroup"/>. The carousel shows these one at a
/// time. The <see cref="Layout"/> decides which card template renders it:
/// StructuredNotes → Anketa, Table → Table, MapLabeling → Comparison, FlatList → Radio/Doble.
/// </summary>
public partial class ListeningCardViewModel : ObservableObject
{
    public int GroupId { get; }
    public QuestionGroupLayout Layout { get; }
    public string? Instruction { get; }
    public string? SharedListTitle { get; }
    public IReadOnlyList<string> SharedOptions { get; }
    public bool HasSharedOptions => SharedOptions.Count > 0;
    public string? ImagePath { get; }
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);

    /// <summary>True when the shared options carry descriptive text (headings "i. …", endings "A. …"),
    /// not just bare tags ("A","B",…). A bare-letter list — e.g. the A–G derived from a "sections A-G"
    /// range — duplicates exactly what the dropdowns already show, so it's not worth a legend box.</summary>
    public bool HasMeaningfulSharedOptions => SharedOptions.Any(o => !BareTagRegex.IsMatch(o.Trim()));

    /// <summary>Comparison/Selector cards show the legend as text only when there is no image to carry
    /// it AND the list is meaningful (otherwise the dropdowns already convey everything).</summary>
    public bool ShowLegend => !HasImage && HasMeaningfulSharedOptions;

    /// <summary>
    /// Doble ("choose TWO/THREE letters") cards: a FlatList of interchangeable MatchingFeatures
    /// slots that all pick from the same pool of lettered statements. That pool is already shown
    /// in full inside every dropdown, so the shared-options legend box would just duplicate it.
    /// </summary>
    public bool IsMultiAnswerSelect { get; }

    /// <summary>The shared-options legend box is useful for matching/headings cards but redundant
    /// (and was reported as clutter) on Doble cards, and pointless when it's just bare letters —
    /// hide it in both cases.</summary>
    public bool ShowSharedListBox => HasMeaningfulSharedOptions && !IsMultiAnswerSelect;

    /// <summary>Raw StructuredNotes markup (Anketa). Parsed by StructuredNotesRenderer.</summary>
    public string? NotesTemplate { get; }
    /// <summary>Raw Table JSON. Parsed by TableRenderer.</summary>
    public string? TableJson { get; }
    /// <summary>Raw Cascade markup (flow chart). Parsed by CascadeRenderer.</summary>
    public string? CascadeMarkup { get; }
    /// <summary>Raw AnketaImage markup (flowing prose with "{N}" letter-dropdown gaps). Parsed by AnketaImageRenderer.</summary>
    public string? AnketaImageMarkup { get; }

    public ObservableCollection<IReadingQuestionViewModel> Questions { get; }

    /// <summary>Gap-input VMs keyed by display number — used by the inline renderers to bind "{N}".</summary>
    public IReadOnlyDictionary<int, TextInputQuestionViewModel> GapByNumber { get; }

    /// <summary>Letter-picker VMs keyed by display number — used by the CascadeImage renderer for inline dropdowns.</summary>
    public IReadOnlyDictionary<int, MatchingQuestionViewModel> MatchByNumber { get; }

    public int PartIndex { get; }
    public int PartOrder { get; }
    public string PartTitle { get; }

    public int FirstQuestionNumber { get; }
    public int LastQuestionNumber { get; }
    public string RangeLabel { get; }
    public string KindLabel { get; }

    /// <summary>Display number of this card within the test (1-based). Set by the session.</summary>
    public int CardNumber { get; set; }

    /// <summary>True when this is the card currently shown in the carousel. Drives the indicator highlight.</summary>
    [ObservableProperty] private bool _isCurrent;

    public ListeningCardViewModel(TestQuestionGroup source, int partIndex, int partOrder, string partTitle)
    {
        GroupId = source.Id;
        Layout = source.Layout;
        Instruction = source.InstructionText;
        SharedListTitle = source.SharedListTitle;
        SharedOptions = ParseStringArray(source.SharedOptionsJson);
        ImagePath = source.ImagePath;
        PartIndex = partIndex;
        PartOrder = partOrder;
        PartTitle = partTitle;

        var isTextGapCard = Layout is QuestionGroupLayout.StructuredNotes
            or QuestionGroupLayout.SummaryFlow
            or QuestionGroupLayout.Table
            or QuestionGroupLayout.MapLabeling
            or QuestionGroupLayout.Cascade;
        var isComboGapCard = Layout is QuestionGroupLayout.CascadeImage
            or QuestionGroupLayout.AnketaImage;

        var gapMap = new Dictionary<int, TextInputQuestionViewModel>();
        var matchMap = new Dictionary<int, MatchingQuestionViewModel>();
        var built = new List<IReadingQuestionViewModel>();
        foreach (var q in source.Questions.OrderBy(x => x.OrderInPart))
        {
            // Three modes:
            // - text-gap cards (Anketa/Table/Comparison/Cascade): user types into a dotted field
            // - combo-gap cards (CascadeImage): user picks a letter from the shared A–H box
            // - choice cards (Radio/Doble): full-size factory (radios / dropdowns), per-question options
            if (isTextGapCard)
            {
                var vm = new TextInputQuestionViewModel(q, q.OrderInPart);
                gapMap[q.OrderInPart] = vm;
                built.Add(vm);
            }
            else if (isComboGapCard)
            {
                var vm = new MatchingQuestionViewModel(q, q.OrderInPart, SharedOptions);
                matchMap[q.OrderInPart] = vm;
                built.Add(vm);
            }
            else
            {
                // Choice (Radio/Doble) and Selector (TFNG / matching dropdowns) cards: questions go
                // through the factory. Pass SharedOptions so Selector matching questions (sections,
                // headings, sentence-endings) get their letter/numeral dropdown items from the group.
                built.Add(ReadingQuestionViewModelFactory.Create(q, q.OrderInPart, SharedOptions));
            }
        }

        Questions = new ObservableCollection<IReadingQuestionViewModel>(built);
        GapByNumber = gapMap;
        MatchByNumber = matchMap;

        FirstQuestionNumber = built.Count > 0 ? built.Min(q => q.DisplayNumber) : 0;
        LastQuestionNumber = built.Count > 0 ? built.Max(q => q.DisplayNumber) : 0;
        RangeLabel = FirstQuestionNumber == LastQuestionNumber
            ? $"Questions {FirstQuestionNumber}"
            : $"Questions {FirstQuestionNumber}–{LastQuestionNumber}";

        // StructuredNotes (bulleted notes) and SummaryFlow (flowing prose) both render via the
        // notes renderer — the difference is only the markup shape, both with inline "{N}" gaps.
        NotesTemplate = Layout is QuestionGroupLayout.StructuredNotes or QuestionGroupLayout.SummaryFlow
            ? source.SummaryTemplate : null;
        TableJson = Layout == QuestionGroupLayout.Table ? source.SummaryTemplate : null;
        // CascadeImage reuses Cascade's "blocks separated by ===" markup, so both layouts feed the field.
        CascadeMarkup = Layout is QuestionGroupLayout.Cascade or QuestionGroupLayout.CascadeImage ? source.SummaryTemplate : null;
        // AnketaImage: flowing prose with inline letter-dropdown gaps (combo-gap, rendered by AnketaImageRenderer).
        AnketaImageMarkup = Layout == QuestionGroupLayout.AnketaImage ? source.SummaryTemplate : null;

        // A "choose TWO letters" (Doble) card is a FlatList of MatchingFeatures slots whose stems are
        // generic placeholders ("First answer", "True statement #1", …) — distinct from a real matching
        // card where each stem is the entity being matched (a name, date, or sentence).
        IsMultiAnswerSelect = Layout == QuestionGroupLayout.FlatList
            && source.Questions.Count > 0
            && source.Questions.All(q => q.Type == QuestionType.MatchingFeatures && IsPlaceholderStem(q.Stem));
        KindLabel = Layout switch
        {
            QuestionGroupLayout.StructuredNotes => Loc.Tr("Listening_KindNotes"),
            QuestionGroupLayout.SummaryFlow => Loc.Tr("Listening_KindSummaryFlow"),
            QuestionGroupLayout.Table => Loc.Tr("Listening_KindTable"),
            QuestionGroupLayout.MapLabeling => Loc.Tr("Listening_KindMapLabeling"),
            QuestionGroupLayout.Cascade => Loc.Tr("Listening_KindCascade"),
            QuestionGroupLayout.CascadeImage => Loc.Tr("Listening_KindCascadeImage"),
            QuestionGroupLayout.Selector => Loc.Tr("Listening_KindSelector"),
            QuestionGroupLayout.AnketaImage => Loc.Tr("Listening_KindAnketaImage"),
            _ => Loc.Tr("Listening_KindDefault")
        };
    }

    // A "bare tag" shared option = just a letter/roman numeral with no descriptive label ("A", "vii.").
    private static readonly Regex BareTagRegex = new(@"^[A-Za-z]{1,3}[\.\)]?$", RegexOptions.Compiled);

    private static readonly Regex PlaceholderStemRegex = new(
        @"^\s*(?:(?:first|second|third|fourth|fifth)\s+answer|answer\s*\d+|true\s+statement\s*#?\s*\d+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsPlaceholderStem(string? stem)
        => string.IsNullOrWhiteSpace(stem) || PlaceholderStemRegex.IsMatch(stem);

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? (IReadOnlyList<string>)Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }
}
