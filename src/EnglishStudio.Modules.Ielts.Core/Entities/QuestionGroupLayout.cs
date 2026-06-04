namespace EnglishStudio.Modules.Ielts.Core.Entities;

/// <summary>
/// Determines how a <see cref="TestQuestionGroup"/> renders its child questions in the UI.
/// </summary>
public enum QuestionGroupLayout
{
    /// <summary>Each child question is rendered as a separate card in a vertical list.</summary>
    FlatList = 1,

    /// <summary>One block of flowing text with inline input slots numbered against child questions.</summary>
    SummaryFlow = 2,

    /// <summary>Question list rendered alongside an image (e.g. map labeling).</summary>
    MapLabeling = 3,

    /// <summary>
    /// Listening "notes/form" card: a structured block (title + sections + bullets) with inline
    /// numbered gaps. The block markup lives in <see cref="TestQuestionGroup.SummaryTemplate"/>.
    /// </summary>
    StructuredNotes = 4,

    /// <summary>
    /// Listening "table" card: a grid of rows/columns with inline numbered gaps in cells. The
    /// grid (columns + rows) is stored as JSON in <see cref="TestQuestionGroup.SummaryTemplate"/>.
    /// </summary>
    Table = 5,

    /// <summary>
    /// Listening "flow chart" card (Cascade): a vertical chain of bordered blocks connected by
    /// downward arrows, each block holding centred lines with inline numbered gaps. Blocks are
    /// stored in <see cref="TestQuestionGroup.SummaryTemplate"/>, separated by lines of "===".
    /// </summary>
    Cascade = 6,

    /// <summary>
    /// Listening "flow chart with letter box" card (CascadeImage): identical to <see cref="Cascade"/>
    /// but preceded by an option-box image (or text legend) of lettered choices A–H, and each inline
    /// gap is a dropdown that picks one of those letters instead of a free-text field. Letter labels
    /// come from <see cref="TestQuestionGroup.SharedOptionsJson"/>; the image (when present) is in
    /// <see cref="TestQuestionGroup.ImagePath"/>; the flow-chart markup is in
    /// <see cref="TestQuestionGroup.SummaryTemplate"/> (same blocks-separated-by-"===" format).
    /// </summary>
    CascadeImage = 7,

    /// <summary>
    /// Reading "Selector" card: an instruction block followed by a vertical list of question rows,
    /// each rendered as a stem plus a dropdown. The dropdown options come from the question type
    /// (TrueFalseNotGiven → TRUE/FALSE/NOT GIVEN, YesNoNotGiven → YES/NO/NOT GIVEN) or, for matching
    /// questions, from a shared letter set in <see cref="TestQuestionGroup.SharedOptionsJson"/>
    /// (e.g. sections A–G, or sentence endings A–F). When the lettered options carry text (sentence
    /// endings), the legend is shown via <see cref="TestQuestionGroup.ImagePath"/> (image) or as a
    /// text legend fallback — this covers the "Selector_Image" source variant.
    /// </summary>
    Selector = 8,

    /// <summary>
    /// Reading "Anketa_Image" card: a flowing summary/notes block (markup in
    /// <see cref="TestQuestionGroup.SummaryTemplate"/> with inline numbered gaps "{N}"), where each
    /// gap is a dropdown that picks a letter from a shared word/option box rather than a free-text
    /// field. Letter labels come from <see cref="TestQuestionGroup.SharedOptionsJson"/>; the option
    /// box is shown via <see cref="TestQuestionGroup.ImagePath"/> (image) or a text legend fallback.
    /// This is the StructuredNotes/SummaryFlow analogue of <see cref="CascadeImage"/>.
    /// </summary>
    AnketaImage = 9
}
