namespace EnglishStudio.Modules.Ielts.Core.Entities;

public enum QuestionType
{
    // Reading + shared
    TrueFalseNotGiven = 1,
    YesNoNotGiven = 2,
    MultipleChoiceSingle = 3,
    MultipleChoiceMulti = 4,
    MatchingHeadings = 5,
    MatchingInformation = 6,
    MatchingFeatures = 7,
    MatchingSentenceEndings = 8,
    SentenceCompletion = 9,
    SummaryCompletion = 10,
    NoteCompletion = 11,
    TableCompletion = 12,
    FlowChartCompletion = 13,
    ShortAnswer = 14,

    // Listening-specific
    FormCompletion = 20,
    MapLabeling = 21,
    DiagramLabeling = 22
}
