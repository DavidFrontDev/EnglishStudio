namespace EnglishStudio.Modules.Dictionary.Entities;

public class Example
{
    public int Id { get; set; }

    public int? WordId { get; set; }
    public Word? Word { get; set; }

    public int? PhrasalVerbId { get; set; }
    public PhrasalVerb? PhrasalVerb { get; set; }

    public int? SenseId { get; set; }
    public Sense? Sense { get; set; }

    public string TextEn { get; set; } = string.Empty;
    public string? TextRu { get; set; }

    public string? Source { get; set; }
}
