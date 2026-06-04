namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// A dictionary word the user added to a text's practice pool from the reader ("🎯 В тренировку").
/// Links a <see cref="ReadingText"/> to a dictionary Word by id. The Word lives in
/// DictionaryDbContext (same physical SQLite file, different context), so <see cref="WordId"/> is a
/// plain logical reference with no FK here. The trainer builds a text-scoped FSRS session from the
/// pool's word ids.
/// </summary>
public class ReadingPracticeItem
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }

    public int WordId { get; set; }

    /// <summary>Snapshot of the word's headword, for showing the pool without a cross-context join.</summary>
    public string Headword { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
