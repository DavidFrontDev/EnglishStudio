namespace EnglishStudio.Modules.Dictionary.Entities;

public enum MediaKind
{
    Audio = 1,
    Image = 2,
}

public class MediaAsset
{
    public int Id { get; set; }

    public int? WordId { get; set; }
    public Word? Word { get; set; }

    public int? SenseId { get; set; }
    public Sense? Sense { get; set; }

    public MediaKind Kind { get; set; }

    public string LocalPath { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string? Locale { get; set; }
    public string? Attribution { get; set; }

    public DateTime CreatedAt { get; set; }
}
