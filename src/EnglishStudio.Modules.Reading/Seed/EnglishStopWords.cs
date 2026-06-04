namespace EnglishStudio.Modules.Reading.Seed;

/// <summary>
/// The most frequent English function words (articles, pronouns, prepositions, conjunctions,
/// auxiliaries, common quantifiers). Pre-teach drops these so the candidate list isn't flooded
/// with words every learner already knows. Matching is case-insensitive on normalized tokens.
/// </summary>
public static class EnglishStopWords
{
    public static readonly IReadOnlySet<string> Words = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Articles & determiners
        "a", "an", "the", "this", "that", "these", "those", "each", "every", "either", "neither",
        "some", "any", "no", "all", "both", "half", "such", "another", "other", "others",
        // Pronouns
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them",
        "my", "your", "his", "its", "our", "their", "mine", "yours", "hers", "ours", "theirs",
        "myself", "yourself", "himself", "herself", "itself", "ourselves", "yourselves", "themselves",
        "who", "whom", "whose", "which", "what", "whatever", "whoever", "whichever",
        "anyone", "everyone", "someone", "no one", "nobody", "anybody", "everybody", "somebody",
        "anything", "everything", "something", "nothing",
        // Prepositions
        "of", "in", "on", "at", "to", "from", "by", "with", "without", "about", "against",
        "between", "among", "through", "during", "before", "after", "above", "below", "under",
        "over", "into", "onto", "upon", "off", "out", "up", "down", "near", "for", "as",
        "per", "via", "within", "toward", "towards", "across", "behind", "beside", "beyond",
        // Conjunctions
        "and", "or", "but", "nor", "so", "yet", "if", "then", "than", "because", "although",
        "though", "while", "whereas", "unless", "until", "since", "whether", "however",
        // Auxiliaries / common verbs
        "be", "am", "is", "are", "was", "were", "been", "being",
        "have", "has", "had", "having", "do", "does", "did", "doing", "done",
        "will", "would", "shall", "should", "can", "could", "may", "might", "must",
        "let", "get", "got",
        // Adverbs / particles
        "not", "very", "too", "also", "just", "only", "even", "still", "here", "there",
        "now", "then", "once", "again", "ever", "never", "always", "often", "sometimes",
        "more", "most", "less", "least", "much", "many", "few", "little", "lot", "lots",
        "well", "quite", "rather", "almost", "yes", "ok", "okay",
        // Misc high-frequency
        "one", "two", "three", "first", "last", "next", "own", "same", "way", "thing", "things",
    };

    public static bool IsStopWord(string normalized) => Words.Contains(normalized);
}
