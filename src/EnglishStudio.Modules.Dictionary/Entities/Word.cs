namespace EnglishStudio.Modules.Dictionary.Entities;

public class Word
{
    public int Id { get; set; }

    public string Headword { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;

    public string? IpaUk { get; set; }
    public string? IpaUs { get; set; }

    public string? AudioUkPath { get; set; }
    public string? AudioUsPath { get; set; }

    public int? FrequencyRank { get; set; }

    public CefrLevel CefrLevel { get; set; } = CefrLevel.Unknown;
    public WordSource Source { get; set; } = WordSource.Unknown;

    /// <summary>
    /// True for entries generated on-demand by Claude (looked up while reading). These are
    /// unverified — surfaced with a 🤖 badge so they can be reviewed.
    /// </summary>
    public bool IsAiGenerated { get; set; }

    public int PartOfSpeechId { get; set; }
    public PartOfSpeech PartOfSpeech { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Sense> Senses { get; set; } = new List<Sense>();
    public ICollection<WordForm> Forms { get; set; } = new List<WordForm>();
    public ICollection<Example> Examples { get; set; } = new List<Example>();
    public ICollection<MediaAsset> Media { get; set; } = new List<MediaAsset>();
    public ICollection<WordCategory> WordCategories { get; set; } = new List<WordCategory>();
    public ICollection<WordTag> WordTags { get; set; } = new List<WordTag>();
}
