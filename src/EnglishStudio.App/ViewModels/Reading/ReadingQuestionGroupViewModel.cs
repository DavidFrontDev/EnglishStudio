using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.ViewModels.Reading.Questions;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading;

/// <summary>
/// View model for one <see cref="TestQuestionGroup"/>. Owns the group's instruction,
/// shared options list (headings/phrases/word box/places), optional image (map),
/// optional pre-filled example, and the child question VMs.
/// </summary>
public partial class ReadingQuestionGroupViewModel : ObservableObject
{
    public int GroupId { get; }
    public QuestionGroupLayout Layout { get; }
    public string? Instruction { get; }
    public string? SharedListTitle { get; }
    public IReadOnlyList<string> SharedOptions { get; }
    public string? ImagePath { get; }
    public string? ExampleStem { get; }
    public string? ExampleAnswer { get; }
    public bool HasExample => !string.IsNullOrWhiteSpace(ExampleAnswer);
    public bool HasSharedOptions => SharedOptions.Count > 0;

    /// <summary>
    /// True when at least one shared option carries a label after its tag (e.g. "A. The Sperrin Mountains").
    /// Used to hide the redundant tag-only list (just "A","B","C",…) on map-labeling groups where the
    /// letters already appear on the map image.
    /// </summary>
    public bool HasMeaningfulSharedList => SharedOptions.Any(s => s.IndexOfAny(new[] { '.', ':', '—', '-', ' ' }) > 0);
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);

    public ObservableCollection<IReadingQuestionViewModel> Questions { get; }
    public IReadOnlyList<SummaryFlowSegment> Segments { get; }

    public int FirstQuestionNumber { get; }
    public int LastQuestionNumber { get; }
    public string RangeLabel { get; }

    public ReadingQuestionGroupViewModel(
        TestQuestionGroup source,
        IReadOnlyList<IReadingQuestionViewModel> questionVms)
    {
        GroupId = source.Id;
        Layout = source.Layout;
        Instruction = source.InstructionText;
        SharedListTitle = source.SharedListTitle;
        SharedOptions = ParseStringArray(source.SharedOptionsJson);
        ImagePath = source.ImagePath;
        ExampleStem = source.ExampleStem;
        ExampleAnswer = source.ExampleAnswer;

        Questions = new ObservableCollection<IReadingQuestionViewModel>(questionVms);
        FirstQuestionNumber = questionVms.Count > 0 ? questionVms.Min(q => q.DisplayNumber) : 0;
        LastQuestionNumber = questionVms.Count > 0 ? questionVms.Max(q => q.DisplayNumber) : 0;
        RangeLabel = FirstQuestionNumber == LastQuestionNumber
            ? $"Questions {FirstQuestionNumber}"
            : $"Questions {FirstQuestionNumber}–{LastQuestionNumber}";

        if (Layout == QuestionGroupLayout.SummaryFlow && !string.IsNullOrEmpty(source.SummaryTemplate))
        {
            var byNumber = Questions.ToDictionary(q => q.DisplayNumber);
            Segments = SummaryFlowParser.Parse(source.SummaryTemplate!, byNumber);
        }
        else
        {
            Segments = Array.Empty<SummaryFlowSegment>();
        }
    }

    /// <summary>
    /// Builds an implicit "flat list" group for a TestPart that has questions but no
    /// explicit <see cref="TestQuestionGroup"/> rows (e.g. legacy data).
    /// </summary>
    public static ReadingQuestionGroupViewModel FromUngroupedQuestions(IReadOnlyList<IReadingQuestionViewModel> questionVms)
    {
        var synthetic = new TestQuestionGroup
        {
            Id = 0,
            Layout = QuestionGroupLayout.FlatList,
            InstructionText = null
        };
        return new ReadingQuestionGroupViewModel(synthetic, questionVms);
    }

    private static IReadOnlyList<string> ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<List<string>>(json);
            return arr ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
