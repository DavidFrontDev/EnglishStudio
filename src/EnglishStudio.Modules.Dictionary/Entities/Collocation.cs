namespace EnglishStudio.Modules.Dictionary.Entities;

public enum CollocationPattern
{
    Unknown = 0,
    VerbNoun = 1,           // make a decision
    AdjectiveNoun = 2,      // severe weather
    VerbAdverb = 3,         // work hard
    AdverbAdjective = 4,    // highly likely
    NounNoun = 5,           // traffic congestion
    NounPrepNoun = 6,       // increase in demand
}

public class Collocation
{
    public int Id { get; set; }

    public int? HeadWordId { get; set; }
    public Word? HeadWord { get; set; }

    public string Headword { get; set; } = string.Empty;
    public string LinkedText { get; set; } = string.Empty;
    public CollocationPattern Pattern { get; set; }

    public string DefinitionEn { get; set; } = string.Empty;
    public string TranslationRu { get; set; } = string.Empty;
    public string? ExampleEn { get; set; }

    public int? FrequencyRank { get; set; }
    public WordSource Source { get; set; } = WordSource.Unknown;

    public DateTime CreatedAt { get; set; }
}
