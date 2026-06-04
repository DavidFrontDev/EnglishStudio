namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// One read-aloud session over a <see cref="ReadingText"/>. Populated from Phase 3
/// (live tracking) onward; fields stay at defaults until then.
/// </summary>
public class ReadingSession
{
    public int Id { get; set; }

    public int ReadingTextId { get; set; }
    public ReadingText ReadingText { get; set; } = null!;

    public DateTime StartedAt { get; set; }

    public int DurationSec { get; set; }

    public int WordsRead { get; set; }

    public double Wpm { get; set; }

    public double AccuracyPct { get; set; }

    /// <summary>Path to the recorded WAV (for Phase 4 Whisper analysis), if kept.</summary>
    public string? AudioPath { get; set; }

    public bool Completed { get; set; }

    public ICollection<ReadingWordStat> WordStats { get; set; } = new List<ReadingWordStat>();
}
