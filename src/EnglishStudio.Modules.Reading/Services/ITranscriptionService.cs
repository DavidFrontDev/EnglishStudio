namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Resolves the IPA transcription shown above words in the reader. Prefers the curated dictionary
/// transcription (UK, with stress) and falls back to CMUdict for the long tail.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Resolves display IPA for each normalized word. Words that cannot be resolved are omitted
    /// from the result (the reader then shows no transcription above them).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> normalizedWords, CancellationToken ct = default);
}
