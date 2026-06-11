using System.Text.Json;
using EnglishStudio.Modules.Dictionary.Data;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.Modules.Dictionary.Images;

public sealed class AppSettings : IAppSettings
{
    private readonly string _path;
    private readonly ILogger<AppSettings> _logger;
    private SettingsState _state = new();

    public AppSettings(ILogger<AppSettings> logger)
    {
        _logger = logger;
        _path = Path.Combine(DictionaryPaths.AppDataRoot, "settings.json");
        Load();
    }

    public AppLanguage Language => _state.Language;
    public string? Theme => Nullify(_state.Theme);
    public string? PexelsApiKey => Nullify(_state.PexelsApiKey);
    public int DailyNewLimit => _state.DailyNewLimit;
    public int DailyReviewLimit => _state.DailyReviewLimit;
    public double TargetRetention => _state.TargetRetention;
    public string? ClaudeCliPath => Nullify(_state.ClaudeCliPath);
    public WhisperModelSize WhisperModel => _state.WhisperModel;
    public string? MicrophoneDeviceName => Nullify(_state.MicrophoneDeviceName);
    public string? ListeningVoiceUkFemale => Nullify(_state.ListeningVoiceUkFemale);
    public string? ListeningVoiceUkMale => Nullify(_state.ListeningVoiceUkMale);
    public string? ListeningVoiceUsFemale => Nullify(_state.ListeningVoiceUsFemale);
    public string? ListeningVoiceUsMale => Nullify(_state.ListeningVoiceUsMale);

    public void Update(SettingsUpdate u)
    {
        _state = _state with
        {
            Language = u.Language ?? _state.Language,
            Theme = u.Theme.HasValue ? u.Theme.Value : _state.Theme,
            PexelsApiKey = u.PexelsApiKey.HasValue ? u.PexelsApiKey.Value : _state.PexelsApiKey,
            DailyNewLimit = u.DailyNewLimit ?? _state.DailyNewLimit,
            DailyReviewLimit = u.DailyReviewLimit ?? _state.DailyReviewLimit,
            TargetRetention = u.TargetRetention ?? _state.TargetRetention,
            ClaudeCliPath = u.ClaudeCliPath.HasValue ? u.ClaudeCliPath.Value : _state.ClaudeCliPath,
            WhisperModel = u.WhisperModel ?? _state.WhisperModel,
            MicrophoneDeviceName = u.MicrophoneDeviceName.HasValue ? u.MicrophoneDeviceName.Value : _state.MicrophoneDeviceName,
            ListeningVoiceUkFemale = u.ListeningVoiceUkFemale.HasValue ? u.ListeningVoiceUkFemale.Value : _state.ListeningVoiceUkFemale,
            ListeningVoiceUkMale = u.ListeningVoiceUkMale.HasValue ? u.ListeningVoiceUkMale.Value : _state.ListeningVoiceUkMale,
            ListeningVoiceUsFemale = u.ListeningVoiceUsFemale.HasValue ? u.ListeningVoiceUsFemale.Value : _state.ListeningVoiceUsFemale,
            ListeningVoiceUsMale = u.ListeningVoiceUsMale.HasValue ? u.ListeningVoiceUsMale.Value : _state.ListeningVoiceUsMale,
        };
        Save();
    }

    private static string? Nullify(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var text = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<SettingsState>(text);
            if (loaded is not null) _state = loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings.json");
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save settings.json");
        }
    }

    private sealed record SettingsState(
        string? PexelsApiKey = null,
        int DailyNewLimit = 20,
        int DailyReviewLimit = 100,
        double TargetRetention = 0.9,
        string? ClaudeCliPath = null,
        WhisperModelSize WhisperModel = WhisperModelSize.Base,
        string? MicrophoneDeviceName = null,
        string? ListeningVoiceUkFemale = null,
        string? ListeningVoiceUkMale = null,
        string? ListeningVoiceUsFemale = null,
        string? ListeningVoiceUsMale = null,
        AppLanguage Language = AppLanguage.Ru,
        string? Theme = null);
}
