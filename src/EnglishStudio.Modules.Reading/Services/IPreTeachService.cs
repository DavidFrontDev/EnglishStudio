namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Pre-teach (F1): surface the unfamiliar words of a text and push them into SRS before
/// reading. Implemented by Agent A; consumed by the reader UI (Agent B).
/// </summary>
public interface IPreTeachService
{
    /// <summary>True when on-demand AI enrichment is possible (Claude CLI present).</summary>
    bool CanEnrich { get; }

    /// <summary>
    /// Computes pre-teach candidates from the text: dictionary frequency/CEFR data, the user's
    /// SRS state, a stop-word filter and the supplied <paramref name="options"/>.
    /// </summary>
    Task<PreTeachResult> AnalyzeAsync(int textId, PreTeachOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Enriches candidates that aren't in the dictionary yet (Claude → persisted entry), then
    /// adds all of them to SRS. Returns the number of words added. Idempotent.
    /// </summary>
    Task<int> AddToTrainingAsync(
        IReadOnlyList<PreTeachCandidate> candidates,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}
