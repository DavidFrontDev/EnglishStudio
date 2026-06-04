namespace EnglishStudio.Modules.Dictionary.Entities;

public class Tag
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameRu { get; set; } = string.Empty;

    public ICollection<WordTag> WordTags { get; set; } = new List<WordTag>();
}

public class WordTag
{
    public int WordId { get; set; }
    public Word Word { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
