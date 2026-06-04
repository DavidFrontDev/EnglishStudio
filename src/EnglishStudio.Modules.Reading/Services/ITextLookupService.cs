namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Resolves a user text selection to a translation card.</summary>
public interface ITextLookupService
{
    /// <summary>True when on-demand AI enrichment is possible (Claude CLI present).</summary>
    bool CanEnrich { get; }

    /// <summary>
    /// Looks up the selected word/phrase: dictionary first, then (for an unknown single
    /// word) Claude enrichment which also persists the entry. <paramref name="contextSentence"/>
    /// helps disambiguate polysemy.
    /// </summary>
    Task<WordLookupResult> LookupAsync(string selectedText, string? contextSentence = null, CancellationToken ct = default);
}
