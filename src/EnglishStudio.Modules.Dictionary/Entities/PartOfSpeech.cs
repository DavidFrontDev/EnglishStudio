namespace EnglishStudio.Modules.Dictionary.Entities;

public class PartOfSpeech
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameRu { get; set; } = string.Empty;

    public ICollection<Word> Words { get; set; } = new List<Word>();
}
