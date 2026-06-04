namespace EnglishStudio.Modules.Reading.Services;

/// <summary>
/// Text-to-speech for shadowing (F4). Implemented by Agent A (System.Speech). Reads a phrase
/// aloud at an adjustable rate, with optional voice selection.
/// </summary>
public interface ITtsService
{
    /// <summary>True when at least one usable English voice is installed.</summary>
    bool IsAvailable { get; }

    /// <summary>Names of the available English voices.</summary>
    IReadOnlyList<string> Voices { get; }

    /// <summary>Speaks the text aloud. <paramref name="rate"/> ~0.75–1.0 (mapped to the engine range).</summary>
    Task SpeakAsync(string text, string? voice = null, double rate = 1.0, CancellationToken ct = default);

    /// <summary>Stops any current speech.</summary>
    Task StopAsync();
}
