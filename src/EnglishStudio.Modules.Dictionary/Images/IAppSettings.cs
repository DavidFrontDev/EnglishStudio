namespace EnglishStudio.Modules.Dictionary.Images;

public interface IAppSettings
{
    string? PexelsApiKey { get; }

    int DailyNewLimit { get; }
    int DailyReviewLimit { get; }
    double TargetRetention { get; }

    // IELTS / M7+ additions
    string? ClaudeCliPath { get; }
    WhisperModelSize WhisperModel { get; }

    /// <summary>Product name of the selected input device; null/empty = system default.</summary>
    string? MicrophoneDeviceName { get; }

    string? ListeningVoiceUkFemale { get; }
    string? ListeningVoiceUkMale { get; }
    string? ListeningVoiceUsFemale { get; }
    string? ListeningVoiceUsMale { get; }

    void Update(SettingsUpdate update);
}

/// <summary>
/// Patch object — only non-null properties are applied.
/// </summary>
public sealed record SettingsUpdate
{
    public Optional<string?> PexelsApiKey { get; init; }
    public int? DailyNewLimit { get; init; }
    public int? DailyReviewLimit { get; init; }
    public double? TargetRetention { get; init; }
    public Optional<string?> ClaudeCliPath { get; init; }
    public WhisperModelSize? WhisperModel { get; init; }
    public Optional<string?> MicrophoneDeviceName { get; init; }
    public Optional<string?> ListeningVoiceUkFemale { get; init; }
    public Optional<string?> ListeningVoiceUkMale { get; init; }
    public Optional<string?> ListeningVoiceUsFemale { get; init; }
    public Optional<string?> ListeningVoiceUsMale { get; init; }
}

/// <summary>
/// Distinguish "not set" (HasValue == false → skip in update) from "set to null" (HasValue == true, Value == null → clear).
/// </summary>
public readonly record struct Optional<T>(bool HasValue, T? Value)
{
    public static Optional<T> Set(T? value) => new(true, value);
    public static Optional<T> NotSet => default;
}
