using System.Collections.ObjectModel;

namespace EnglishStudio.App.ViewModels.Reading;

/// <summary>One row of the "Question Map": header for a passage and the cells of its questions.</summary>
public sealed class QuestionMapPartViewModel
{
    public int PartIndex { get; }
    public int PartOrder { get; }
    public string Title { get; }
    public int FirstQuestionNumber { get; }
    public int LastQuestionNumber { get; }
    public ObservableCollection<QuestionMapCellViewModel> Cells { get; }

    public string RangeLabel => FirstQuestionNumber == LastQuestionNumber
        ? $"Q{FirstQuestionNumber}"
        : $"Q{FirstQuestionNumber}–{LastQuestionNumber}";

    public string Header => $"Passage {PartOrder}: {Title}  ·  {RangeLabel}";

    public QuestionMapPartViewModel(int partIndex, int partOrder, string title, IEnumerable<QuestionMapCellViewModel> cells)
    {
        PartIndex = partIndex;
        PartOrder = partOrder;
        Title = title;
        Cells = new ObservableCollection<QuestionMapCellViewModel>(cells);
        FirstQuestionNumber = Cells.Count > 0 ? Cells.Min(c => c.DisplayNumber) : 0;
        LastQuestionNumber = Cells.Count > 0 ? Cells.Max(c => c.DisplayNumber) : 0;
    }
}
