namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// A user note attached to a highlighted span of a <see cref="ReadingText"/> (F5).
/// <see cref="StartOffset"/>/<see cref="Length"/> are character offsets into the text's BodyText
/// (the same coordinates the tokenizer uses).
/// </summary>
public class TextNote
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }

    public int StartOffset { get; set; }

    public int Length { get; set; }

    /// <summary>The highlighted excerpt, stored for display without re-reading the body.</summary>
    public string Quote { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    /// <summary>Optional highlight colour (e.g. a hex string); null = default.</summary>
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }
}
