namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Shadowing (F4): split a text into sentences and score the user's spoken repeat of one.
/// Implemented by Agent A (reuses the read-along capture + Whisper analysis). The UI records
/// the repeat WAV via IReadingAudioCapture and passes its path here.
/// </summary>
public interface IShadowingService
{
    IReadOnlyList<ShadowingSentence> GetSentences(IReadOnlyList<TextToken> tokens);

    Task<ShadowingScore?> ScoreRepeatAsync(
        string wavPath,
        ShadowingSentence sentence,
        IReadOnlyList<TextToken> tokens,
        CancellationToken ct = default);
}
