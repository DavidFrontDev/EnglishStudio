namespace EnglishStudio.Modules.Dictionary.Entities;

public class Translation
{
    public int Id { get; set; }

    public int SenseId { get; set; }
    public Sense Sense { get; set; } = null!;

    public string TextRu { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
}
