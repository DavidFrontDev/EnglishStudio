namespace EnglishStudio.Modules.Dictionary.Entities;

public class Sense
{
    public int Id { get; set; }

    public int? WordId { get; set; }
    public Word? Word { get; set; }

    public int? PhrasalVerbId { get; set; }
    public PhrasalVerb? PhrasalVerb { get; set; }

    public string DefinitionEn { get; set; } = string.Empty;
    public string DefinitionRu { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public ICollection<Translation> Translations { get; set; } = new List<Translation>();
    public ICollection<Example> Examples { get; set; } = new List<Example>();
    public ICollection<MediaAsset> Media { get; set; } = new List<MediaAsset>();
}
