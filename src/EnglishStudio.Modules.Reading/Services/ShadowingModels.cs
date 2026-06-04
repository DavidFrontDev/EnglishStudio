namespace EnglishStudio.Modules.Reading.Services;

/// <summary>A sentence of the text for shadowing. Word-index range maps to <see cref="TextToken.WordIndex"/>.</summary>
public sealed record ShadowingSentence(int Index, string Text, int StartWordIndex, int EndWordIndex);

/// <summary>Score of one shadowing repeat (reuses <see cref="ReadingWordOutcome"/> from the read-along analysis).</summary>
public sealed record ShadowingScore(
    double AccuracyPct,
    int WordsExpected,
    int WordsSpoken,
    IReadOnlyList<ReadingWordOutcome> Words);
