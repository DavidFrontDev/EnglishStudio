using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.Modules.Reading.Services;

/// <summary>Tuning for which words a text's pre-teach step surfaces.</summary>
public sealed record PreTeachOptions(
    int MaxWords = 30,
    CefrLevel MinCefr = CefrLevel.B1,
    bool OnlyNotInTraining = true);

/// <summary>
/// A word proposed for pre-teaching before reading. <see cref="WordId"/> is null when the word
/// isn't in the dictionary yet (will be enriched via Claude on add).
/// </summary>
public sealed record PreTeachCandidate(
    string Headword,
    string Lemma,
    string? TranslationRu,
    CefrLevel Cefr,
    int? WordId,
    bool InDictionary,
    bool AlreadyInTraining,
    int Occurrences);

/// <summary>Result of analysing a text for pre-teach candidates.</summary>
public sealed record PreTeachResult(
    int TextId,
    IReadOnlyList<PreTeachCandidate> Candidates,
    int TotalDistinctWords,
    int KnownCount);
