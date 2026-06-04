using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Content;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Images;
// Both EnglishStudio.App.Audio and …Dictionary.Images declare WhisperModelSize; settings use the Images one.
using WhisperModelSize = EnglishStudio.Modules.Dictionary.Images.WhisperModelSize;

namespace EnglishStudio.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettings _settings;
    private readonly IClaudeCliClient _claudeCli;
    private readonly IMicrophoneTester _micTester;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ContentImportLauncher _importLauncher;

    [ObservableProperty] private string? _pexelsApiKey;
    [ObservableProperty] private int _dailyNewLimit;
    [ObservableProperty] private int _dailyReviewLimit;
    [ObservableProperty] private double _targetRetention;

    [ObservableProperty] private string? _claudeCliPath;
    [ObservableProperty] private string _claudeCliStatus = "Проверка…";

    [ObservableProperty] private WhisperModelSize _whisperModel;

    // Microphone
    [ObservableProperty] private MicrophoneDeviceInfo? _selectedMicrophone;
    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private bool _isMicTesting;
    [ObservableProperty] private bool _hasTestRecording;
    [ObservableProperty] private string? _micStatus;
    private string? _testRecordingPath;

    [ObservableProperty] private string? _listeningVoiceUkFemale;
    [ObservableProperty] private string? _listeningVoiceUkMale;
    [ObservableProperty] private string? _listeningVoiceUsFemale;
    [ObservableProperty] private string? _listeningVoiceUsMale;

    [ObservableProperty] private string? _saveStatus;

    public ObservableCollection<WhisperOption> WhisperOptions { get; } =
    [
        new(WhisperModelSize.Base, "Base — быстро (142 МБ), для тренировки произношения"),
        new(WhisperModelSize.Medium, "Medium — точно (1.5 ГБ), для Speaking long-form")
    ];

    public ObservableCollection<MicrophoneDeviceInfo> Microphones { get; } = new();

    public SettingsViewModel(
        IAppSettings settings,
        IClaudeCliClient claudeCli,
        IMicrophoneTester micTester,
        IAudioPlayer audioPlayer,
        ContentImportLauncher importLauncher)
    {
        _settings = settings;
        _claudeCli = claudeCli;
        _micTester = micTester;
        _audioPlayer = audioPlayer;
        _importLauncher = importLauncher;
        _micTester.LevelChanged += OnMicLevelChanged;

        _pexelsApiKey = settings.PexelsApiKey;
        _dailyNewLimit = settings.DailyNewLimit;
        _dailyReviewLimit = settings.DailyReviewLimit;
        _targetRetention = settings.TargetRetention;
        _claudeCliPath = settings.ClaudeCliPath;
        _whisperModel = settings.WhisperModel;
        _listeningVoiceUkFemale = settings.ListeningVoiceUkFemale;
        _listeningVoiceUkMale = settings.ListeningVoiceUkMale;
        _listeningVoiceUsFemale = settings.ListeningVoiceUsFemale;
        _listeningVoiceUsMale = settings.ListeningVoiceUsMale;

        LoadMicrophones();
        _ = RefreshClaudeStatusAsync();
    }

    private void OnMicLevelChanged(object? sender, double level) => MicLevel = level;

    private void LoadMicrophones()
    {
        Microphones.Clear();
        var devices = _micTester.GetDevices();
        foreach (var d in devices) Microphones.Add(d);

        var savedName = _settings.MicrophoneDeviceName;
        SelectedMicrophone =
            (savedName is not null ? devices.FirstOrDefault(d => d.Name == savedName) : null)
            ?? devices.FirstOrDefault();

        MicStatus = Microphones.Count <= 1
            ? "Микрофоны не обнаружены. Подключите устройство и нажмите ⟳."
            : null;
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        StopMicTest();
        LoadMicrophones();
    }

    [RelayCommand]
    private void ToggleMicTest()
    {
        if (IsMicTesting) StopMicTest();
        else StartMicTest();
    }

    private void StartMicTest()
    {
        _audioPlayer.Stop();
        HasTestRecording = false;
        _testRecordingPath = null;

        var device = SelectedMicrophone?.DeviceNumber ?? -1;
        _micTester.Start(device, record: true);

        if (_micTester.IsActive)
        {
            IsMicTesting = true;
            MicStatus = "Идёт проверка — говорите в микрофон…";
        }
        else
        {
            IsMicTesting = false;
            MicStatus = "Не удалось открыть микрофон. Выберите другое устройство.";
        }
    }

    private void StopMicTest()
    {
        if (!IsMicTesting) return;

        _testRecordingPath = _micTester.Stop();
        IsMicTesting = false;
        MicLevel = 0;
        HasTestRecording = _testRecordingPath is not null && File.Exists(_testRecordingPath);
        MicStatus = HasTestRecording
            ? "Запись готова — нажмите «▶ Прослушать запись»."
            : "Проверка остановлена.";
    }

    [RelayCommand]
    private void PlayTestRecording()
    {
        if (_testRecordingPath is not null && File.Exists(_testRecordingPath))
            _audioPlayer.Play(_testRecordingPath);
    }

    partial void OnSelectedMicrophoneChanged(MicrophoneDeviceInfo? value)
    {
        // Restart the live test on the newly chosen device for instant feedback.
        if (!IsMicTesting) return;
        _micTester.Stop();
        HasTestRecording = false;
        _testRecordingPath = null;
        _micTester.Start(value?.DeviceNumber ?? -1, record: true);
    }

    /// <summary>Releases the microphone and stops playback when the settings window closes.</summary>
    public void Cleanup()
    {
        _micTester.LevelChanged -= OnMicLevelChanged;
        try { _micTester.Dispose(); } catch { /* ignore */ }
        try { _audioPlayer.Stop(); } catch { /* ignore */ }
        IsMicTesting = false;
        MicLevel = 0;
    }

    [RelayCommand]
    private async Task RefreshClaudeStatusAsync()
    {
        ClaudeCliStatus = "Проверка…";
        await _claudeCli.RefreshAsync();
        if (_claudeCli.IsAvailable)
        {
            var version = _claudeCli.Version ?? "(версия не определена)";
            ClaudeCliStatus = $"✓ Найден: {_claudeCli.ExecutablePath}  ({version})";
        }
        else
        {
            ClaudeCliStatus = "✗ Не найден. Установите Claude CLI или укажите путь вручную.";
        }
    }

    [RelayCommand]
    private void Save()
    {
        _settings.Update(new SettingsUpdate
        {
            PexelsApiKey = Optional<string?>.Set(string.IsNullOrWhiteSpace(PexelsApiKey) ? null : PexelsApiKey.Trim()),
            DailyNewLimit = Math.Clamp(DailyNewLimit, 0, 1000),
            DailyReviewLimit = Math.Clamp(DailyReviewLimit, 0, 5000),
            TargetRetention = Math.Clamp(TargetRetention, 0.7, 0.99),
            ClaudeCliPath = Optional<string?>.Set(string.IsNullOrWhiteSpace(ClaudeCliPath) ? null : ClaudeCliPath.Trim()),
            WhisperModel = WhisperModel,
            // System-default device is stored as null so it always resolves to the OS default.
            MicrophoneDeviceName = Optional<string?>.Set(
                SelectedMicrophone is null || SelectedMicrophone.IsSystemDefault ? null : SelectedMicrophone.Name),
            ListeningVoiceUkFemale = Optional<string?>.Set(ListeningVoiceUkFemale),
            ListeningVoiceUkMale = Optional<string?>.Set(ListeningVoiceUkMale),
            ListeningVoiceUsFemale = Optional<string?>.Set(ListeningVoiceUsFemale),
            ListeningVoiceUsMale = Optional<string?>.Set(ListeningVoiceUsMale)
        });

        SaveStatus = "Сохранено.";
        _ = RefreshClaudeStatusAsync();
    }

    /// <summary>Opens the content-pack import window (also reachable from each module's banner).</summary>
    [RelayCommand]
    private void OpenContentImport() => _importLauncher.Show();

    [RelayCommand]
    private void ClearPexels()
    {
        PexelsApiKey = null;
        _settings.Update(new SettingsUpdate { PexelsApiKey = Optional<string?>.Set(null) });
        SaveStatus = "Pexels ключ очищен.";
    }
}

public sealed record WhisperOption(WhisperModelSize Size, string Label);
