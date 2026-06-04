namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// A persistent colour highlight over a span of a <see cref="ReadingText"/>, created from the
/// reader's translation popup ("🖍 Выделить"). Independent of notes and of the practice pool —
/// purely visual, kept until the user removes it. Offsets are character offsets into BodyText
/// (the same coordinates the tokenizer and notes use).
/// </summary>
public class TextHighlight
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }

    public int StartOffset { get; set; }

    public int Length { get; set; }

    /// <summary>The highlighted excerpt, stored for display without re-reading the body.</summary>
    public string Quote { get; set; } = string.Empty;

    /// <summary>Highlight colour as a hex string; null = the default purple.</summary>
    public string? Color { get; set; }

    public DateTime CreatedAt { get; set; }
}
