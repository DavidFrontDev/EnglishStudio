namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// English pronunciation lexicon (CMUdict): word → ARPAbet phonemes, ARPAbet → IPA, and a
/// "hard for Russian speakers" check. Implemented by Agent A.
/// </summary>
public interface IPronunciationLexicon
{
    /// <summary>Looks up the ARPAbet phoneme sequence for a word (case-insensitive).</summary>
    bool TryGetArpabet(string word, out IReadOnlyList<string> phonemes);

    /// <summary>Renders an ARPAbet sequence as an IPA string.</summary>
    string ToIpa(IReadOnlyList<string> arpabet);

    /// <summary>
    /// Best-effort IPA for displaying above a word: a direct CMUdict hit, else a British→American
    /// spelling variant, else a morphological guess (affix stripped, stem looked up). Returns null
    /// when nothing reasonable can be produced. Does not affect <see cref="TryGetArpabet"/>.
    /// </summary>
    string? GetDisplayIpa(string word);

    /// <summary>True for phonemes commonly mispronounced by Russian speakers (θ, ð, w, æ, ŋ, ɹ, …).</summary>
    bool IsTrickyForRu(string arpabet);
}
