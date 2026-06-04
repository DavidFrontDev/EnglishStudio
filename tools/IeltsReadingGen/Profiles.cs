using System.Text;

namespace EnglishStudio.IeltsReadingGen;

/// <summary>
/// Question-type distribution per passage for each profile. We cycle through 4 profiles
/// across the 30 tests so that every test exercises a different mix of the 14 reading
/// question types — all 14 are covered at least once per cycle.
/// </summary>
internal static class Profiles
{
    public static QuestionSpec[] For(Profile p) => p switch
    {
        Profile.A =>
        [
            new(1, "5 TrueFalseNotGiven, 5 SentenceCompletion, 3 ShortAnswer"),
            new(2, "4 MultipleChoiceSingle, 2 MultipleChoiceMulti, 5 MatchingHeadings, 3 MatchingInformation"),
            new(3, "4 YesNoNotGiven, 3 MatchingFeatures, 3 SummaryCompletion, 3 TableCompletion"),
        ],
        Profile.B =>
        [
            new(1, "4 YesNoNotGiven, 4 SentenceCompletion, 5 ShortAnswer"),
            new(2, "5 MultipleChoiceSingle, 3 MultipleChoiceMulti, 6 MatchingHeadings"),
            new(3, "4 TrueFalseNotGiven, 5 SummaryCompletion, 4 MatchingFeatures"),
        ],
        Profile.C =>
        [
            new(1, "5 TrueFalseNotGiven, 4 SentenceCompletion, 4 MatchingHeadings"),
            new(2, "4 MultipleChoiceSingle, 3 MultipleChoiceMulti, 4 MatchingInformation, 3 MatchingSentenceEndings"),
            new(3, "5 NoteCompletion, 4 TableCompletion, 4 ShortAnswer"),
        ],
        Profile.D =>
        [
            new(1, "5 SentenceCompletion, 4 TrueFalseNotGiven, 4 ShortAnswer"),
            new(2, "4 MultipleChoiceSingle, 2 MultipleChoiceMulti, 4 MatchingHeadings, 4 FlowChartCompletion"),
            new(3, "5 YesNoNotGiven, 4 NoteCompletion, 4 SummaryCompletion"),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(p))
    };
}

internal sealed record QuestionSpec(int PartOrder, string Distribution);
