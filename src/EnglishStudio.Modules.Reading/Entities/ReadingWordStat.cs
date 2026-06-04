namespace EnglishStudio.Modules.Reading.Entities;

/// <summary>
/// Per-word outcome of a reading session (skipped / mispronounced / score).
/// Populated from Phase 4 (Whisper post-analysis) onward.
/// </summary>
public class ReadingWordStat
{
    public int Id { get; set; }

    public int ReadingSessionId { get; set; }
    public ReadingSession ReadingSession { get; set; } = null!;

    /// <summary>Index of the word token within the text.</summary>
    public int TokenIndex { get; set; }

    public bool Skipped { get; set; }

    public bool Mispronounced { get; set; }

    /// <summary>Pronunciation score 0..100, if assessed.</summary>
    public double? Score { get; set; }
}
