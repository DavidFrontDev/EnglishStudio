namespace EnglishStudio.Modules.Reading.Services;

/// <summary>One phoneme of a word: ARPAbet code, its IPA glyph, and whether it's typically
/// hard for a Russian speaker (highlighted in the UI).</summary>
public sealed record PhonemeUnit(string Arpabet, string Ipa, bool IsTrickyForRu);

/// <summary>Phoneme / IPA breakdown of a word. <see cref="Found"/> is false when the word
/// isn't in the pronunciation lexicon.</summary>
public sealed record WordPronunciationGuide(
    string Word,
    string Ipa,
    IReadOnlyList<PhonemeUnit> Units,
    bool Found);

public enum PhonemeDiffKind
{
    Match,
    Substitution,
    Deletion,
    Insertion
}

/// <summary>One step of a phoneme-level alignment between the intended and the recognized word.</summary>
public sealed record PhonemeDiff(PhonemeDiffKind Kind, string? Reference, string? Said);

/// <summary>
/// Best-effort comparison of the intended word vs what was recognized, at the phoneme level.
/// <see cref="HasData"/> is false when either word lacks phoneme data.
/// </summary>
public sealed record WordPhonemeFeedback(
    string Word,
    string RecognizedWord,
    IReadOnlyList<PhonemeDiff> Diffs,
    string FeedbackRu,
    bool HasData);
