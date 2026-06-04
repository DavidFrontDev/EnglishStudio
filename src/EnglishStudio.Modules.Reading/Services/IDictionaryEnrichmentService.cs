namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Fills gaps in the dictionary on demand via Claude. Used by the reader when the user
/// selects a word/phrase that isn't in the local dictionary.
/// </summary>
public interface IDictionaryEnrichmentService
{
    /// <summary>True when the Claude CLI is available (online enrichment possible).</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Asks Claude for a full dictionary entry for <paramref name="lemma"/> and persists it
    /// (Source = Ai, IsAiGenerated = true). Returns the new/existing word id, or null if the
    /// CLI is unavailable, the word isn't a real lexical entry, or generation failed.
    /// </summary>
    Task<int?> FetchAndPersistWordAsync(string lemma, string? contextSentence, CancellationToken ct = default);

    /// <summary>Translates an English phrase to Russian (ephemeral, not persisted). Null on failure.</summary>
    Task<string?> TranslatePhraseAsync(string phrase, string? contextSentence, CancellationToken ct = default);
}
