namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// "Continue from here" bookmark for a <see cref="ReadingText"/> (F5). At most one per text
/// (enforced by a unique index on <see cref="ReadingTextId"/>). <see cref="WordIndex"/> is in
/// the same space as <see cref="TextToken.WordIndex"/>.
/// </summary>
public class TextBookmark
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }

    public int WordIndex { get; set; }

    public DateTime CreatedAt { get; set; }
}
