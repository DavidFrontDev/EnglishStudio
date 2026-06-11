using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using EnglishStudio.App.Localization;
using EnglishStudio.App.ViewModels.Reading.Questions;
using EnglishStudio.Modules.Ielts.Core.Entities;

namespace EnglishStudio.App.ViewModels.Reading;

public partial class ReadingPartViewModel : ObservableObject
{
    public int Order { get; }
    public string Title { get; }
    public string Body { get; }
    public string? IntroNoteRu { get; }
    public IReadOnlyList<PassageParagraphViewModel> Paragraphs { get; }

    /// <summary>Groups of questions (instruction, shared options, optional image/example) in display order.</summary>
    public ObservableCollection<ReadingQuestionGroupViewModel> Groups { get; }

    /// <summary>Flat collection of every question VM in this part — built once for fast iteration.</summary>
    public IReadOnlyList<IReadingQuestionViewModel> Questions { get; }

    public int FirstQuestionNumber { get; }
    public int LastQuestionNumber { get; }
    public string RangeLabel => FirstQuestionNumber == LastQuestionNumber
        ? Loc.Format("ReadIelts_QuestionSingle", FirstQuestionNumber)
        : Loc.Format("ReadIelts_QuestionRange", FirstQuestionNumber, LastQuestionNumber);

    public ReadingPartViewModel(TestPart part, int startingQuestionNumber)
    {
        Order = part.OrderInTest;
        Title = part.Title;
        Body = part.BodyText ?? string.Empty;
        IntroNoteRu = part.IntroNoteRu;
        Paragraphs = PassageParagraphViewModel.Parse(Body);

        var allQuestions = new List<IReadingQuestionViewModel>();
        Groups = new ObservableCollection<ReadingQuestionGroupViewModel>();

        var displayCounter = startingQuestionNumber;

        if (part.Groups is { Count: > 0 })
        {
            // Modern data: questions live inside explicit groups.
            foreach (var group in part.Groups.OrderBy(g => g.OrderInPart))
            {
                var sharedOptions = ParseStringArray(group.SharedOptionsJson);
                var groupQuestionVms = group.Questions
                    .OrderBy(q => q.OrderInPart)
                    .Select(q =>
                    {
                        var vm = ReadingQuestionViewModelFactory.Create(q, displayCounter, sharedOptions);
                        displayCounter++;
                        allQuestions.Add(vm);
                        return vm;
                    })
                    .ToList();

                Groups.Add(new ReadingQuestionGroupViewModel(group, groupQuestionVms));
            }
        }
        else
        {
            // Legacy fallback: part has questions but no groups — wrap them all in one synthetic FlatList group.
            var legacyVms = part.Questions
                .OrderBy(q => q.OrderInPart)
                .Select(q =>
                {
                    var vm = ReadingQuestionViewModelFactory.Create(q, displayCounter, sharedOptions: null);
                    displayCounter++;
                    allQuestions.Add(vm);
                    return vm;
                })
                .ToList();

            if (legacyVms.Count > 0)
            {
                Groups.Add(ReadingQuestionGroupViewModel.FromUngroupedQuestions(legacyVms));
            }
        }

        Questions = allQuestions;
        FirstQuestionNumber = startingQuestionNumber;
        LastQuestionNumber = displayCounter - 1;
    }

    private static IReadOnlyList<string>? ParseStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
