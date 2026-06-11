using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Audio;
using EnglishStudio.App.Content;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Theming;
using EnglishStudio.Modules.Ai;
using EnglishStudio.Modules.Dictionary.Images;
using EnglishStudio.Modules.Dictionary.Srs;
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
    private readonly ILocalizer _localizer;
    private readonly IThemeManager _themeManager;
    private readonly FsrsParameters _fsrsParameters;

    /// <summary>Colour theme. Changing it re-skins the whole UI live and persists the choice.</summary>
    [ObservableProperty] private AppTheme _theme;

    /// <summary>A theme choice with a label resolved in the current language.</summary>
    public sealed record ThemeChoice(AppTheme Theme, string Label);

    /// <summary>Theme options with localized names; rebuilt on language change (the language
    /// selector lives in this same window).</summary>
    public ObservableCollection<ThemeChoice> Themes { get; } = new();

    [ObservableProperty] private string? _pexelsApiKey;
    [ObservableProperty] private int _dailyNewLimit;
    [ObservableProperty] private int _dailyReviewLimit;
    [ObservableProperty] private double _targetRetention;

    [ObservableProperty] private string? _claudeCliPath;
    [ObservableProperty] private string _claudeCliStatus = Loc.Tr("Settings_Checking");

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
    [ObservableProperty] private string? _backupStatus;

    /// <summary>Whisper model options with localized labels; rebuilt on language change.</summary>
    public ObservableCollection<WhisperOption> WhisperOptions { get; } = new();

    public ObservableCollection<MicrophoneDeviceInfo> Microphones { get; } = new();

    public SettingsViewModel(
        IAppSettings settings,
        IClaudeCliClient claudeCli,
        IMicrophoneTester micTester,
        IAudioPlayer audioPlayer,
        ContentImportLauncher importLauncher,
        ILocalizer localizer,
        IThemeManager themeManager,
        FsrsParameters fsrsParameters)
    {
        _settings = settings;
        _claudeCli = claudeCli;
        _micTester = micTester;
        _audioPlayer = audioPlayer;
        _importLauncher = importLauncher;
        _localizer = localizer;
        _themeManager = themeManager;
        _fsrsParameters = fsrsParameters;

        // Seed from the live theme so OnThemeChanged does not re-apply at load.
        _theme = themeManager.Current;

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

        RebuildLocalizedLists();
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

        _micTester.LevelChanged += OnMicLevelChanged;
        LoadMicrophones();
        _ = RefreshClaudeStatusAsync();
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => RebuildLocalizedLists();

    /// <summary>(Re)builds the language-dependent combo lists, preserving the current selection
    /// (the bound <see cref="Theme"/> / <see cref="WhisperModel"/> re-match by value).</summary>
    private void RebuildLocalizedLists()
    {
        Themes.Clear();
        foreach (var t in AppThemes.All)
            Themes.Add(new ThemeChoice(t.Theme, Loc.Tr(t.LabelKey)));

        WhisperOptions.Clear();
        WhisperOptions.Add(new WhisperOption(WhisperModelSize.Base, Loc.Tr("Settings_WhisperBase")));
        WhisperOptions.Add(new WhisperOption(WhisperModelSize.Medium, Loc.Tr("Settings_WhisperMedium")));

        OnPropertyChanged(nameof(Theme));
        OnPropertyChanged(nameof(WhisperModel));
    }

    partial void OnThemeChanged(AppTheme value)
    {
        // Live re-skin: swap palette MergedDictionaries[0] so every DynamicResource repaints, then persist.
        _themeManager.Apply(value);
        _settings.Update(new SettingsUpdate { Theme = Optional<string?>.Set(value.ToString()) });
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
            ? Loc.Tr("Settings_NoMicsFound")
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
            MicStatus = Loc.Tr("Settings_MicTesting");
        }
        else
        {
            IsMicTesting = false;
            MicStatus = Loc.Tr("Settings_MicOpenFailed");
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
            ? Loc.Tr("Settings_RecordingReady")
            : Loc.Tr("Settings_TestStopped");
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
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
        _micTester.LevelChanged -= OnMicLevelChanged;
        try { _micTester.Dispose(); } catch { /* ignore */ }
        try { _audioPlayer.Stop(); } catch { /* ignore */ }
        IsMicTesting = false;
        MicLevel = 0;
    }

    [RelayCommand]
    private async Task RefreshClaudeStatusAsync()
    {
        ClaudeCliStatus = Loc.Tr("Settings_Checking");
        await _claudeCli.RefreshAsync();
        if (_claudeCli.IsAvailable)
        {
            var version = _claudeCli.Version ?? Loc.Tr("Settings_VersionUnknown");
            ClaudeCliStatus = Loc.Format("Settings_ClaudeFound", _claudeCli.ExecutablePath ?? string.Empty, version);
        }
        else
        {
            ClaudeCliStatus = Loc.Tr("Settings_ClaudeNotFound");
        }
    }

    [RelayCommand]
    private void Save()
    {
        var targetRetention = Math.Clamp(TargetRetention, 0.7, 0.99);
        _settings.Update(new SettingsUpdate
        {
            PexelsApiKey = Optional<string?>.Set(string.IsNullOrWhiteSpace(PexelsApiKey) ? null : PexelsApiKey.Trim()),
            DailyNewLimit = Math.Clamp(DailyNewLimit, 0, 1000),
            DailyReviewLimit = Math.Clamp(DailyReviewLimit, 0, 5000),
            TargetRetention = targetRetention,
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

        _fsrsParameters.TargetRetention = targetRetention;

        SaveStatus = _localizer["Settings_Saved"];
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
        SaveStatus = Loc.Tr("Settings_PexelsCleared");
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            var path = await Task.Run(() => Diagnostics.DatabaseBackup.Create("manual"));
            BackupStatus = path is null
                ? Loc.Tr("Settings_BackupNoDb")
                : Loc.Format("Settings_BackupDone", path);
        }
        catch (Exception ex)
        {
            BackupStatus = Loc.Format("Settings_BackupFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Diagnostics.DatabaseBackup.BackupDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Diagnostics.DatabaseBackup.BackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            BackupStatus = Loc.Format("Settings_BackupFailed", ex.Message);
        }
    }
}

public sealed record WhisperOption(WhisperModelSize Size, string Label);
