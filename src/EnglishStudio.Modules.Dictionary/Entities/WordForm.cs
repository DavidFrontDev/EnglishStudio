namespace EnglishStudio.Modules.Dictionary.Entities;

public enum WordFormKind
{
    Unknown = 0,
    PastSimple = 1,
    PastParticiple = 2,
    PresentParticiple = 3,
    ThirdPersonSingular = 4,
    Plural = 5,
    Comparative = 6,
    Superlative = 7,
    Negative = 8,
}

public class WordForm
{
    public int Id { get; set; }

    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    public string Form { get; set; } = string.Empty;
    public WordFormKind Kind { get; set; }
    public bool IsIrregular { get; set; }
}
