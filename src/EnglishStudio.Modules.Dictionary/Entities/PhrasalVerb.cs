namespace EnglishStudio.Modules.Dictionary.Entities;

public class PhrasalVerb
{
    public int Id { get; set; }

    public string Headword { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;

    public int? BaseWordId { get; set; }
    public Word? BaseWord { get; set; }

    public string Particle { get; set; } = string.Empty;

    public CefrLevel CefrLevel { get; set; } = CefrLevel.Unknown;
    public WordSource Source { get; set; } = WordSource.Unknown;
    public int? FrequencyRank { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Sense> Senses { get; set; } = new List<Sense>();
    public ICollection<Example> Examples { get; set; } = new List<Example>();
}
