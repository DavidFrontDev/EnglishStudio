using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Content;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.Modules.Dictionary.Content;
using EnglishStudio.Modules.Ielts.Mock;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.Mock;

/// <summary>
/// Hub полного mock-экзамена + роутер модуля (держит <see cref="CurrentScreen"/>).
/// Список Cambridge-бандлов, выбор режима, баннер «Продолжить» (resume), история попыток.
/// </summary>
public partial class MockHubViewModel : ObservableObject
{
    private readonly IMockSessionService _mock;
    private readonly IContentStore _content;
    private readonly ContentImportLauncher _importLauncher;
    private readonly IServiceProvider _services;
    private readonly ILogger<MockHubViewModel> _log;

    public ObservableCollection<MockBundleCard> Bundles { get; } = new();
    public ObservableCollection<MockAttemptSummary> RecentAttempts { get; } = new();

    [ObservableProperty] private MockBundleCard? _selectedBundle;
    [ObservableProperty] private MockMode _selectedMode = MockMode.CambridgeBundle;
    [ObservableProperty] private MockAttemptSummary? _resumableMock;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    /// <summary>True when one or more of the four IELTS sections aren't imported — shows the banner.</summary>
    [ObservableProperty] private bool _isContentMissing;

    [ObservableProperty] private string _contentMissingText =
        Loc.Tr("Mock_ContentMissingDefault");

    /// <summary>null = виден Hub; иначе — активный под-экран (сессия/результат).</summary>
    [ObservableProperty] private object? _currentScreen;

    public bool IsHubVisible => CurrentScreen is null;

    public MockHubViewModel(
        IMockSessionService mock,
        IContentStore content,
        ContentImportLauncher importLauncher,
        IServiceProvider services,
        ILogger<MockHubViewModel> log)
    {
        _mock = mock;
        _content = content;
        _importLauncher = importLauncher;
        _services = services;
        _log = log;
        _ = LoadAsync();
    }

    /// <summary>Opens the content importer, then reloads (picks up freshly imported content).</summary>
    [RelayCommand]
    private async Task OpenImportAsync()
    {
        _importLauncher.Show();
        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = string.Empty;
        try
        {
            var missing = new List<string>();
            if (!_content.IsImported(ContentSection.Reading)) missing.Add("Reading");
            if (!_content.IsImported(ContentSection.Listening)) missing.Add("Listening");
            if (!_content.IsImported(ContentSection.Writing)) missing.Add("Writing");
            if (!_content.IsImported(ContentSection.Speaking)) missing.Add("Speaking");

            IsContentMissing = missing.Count > 0;
            if (IsContentMissing)
            {
                ContentMissingText =
                    Loc.Format("Mock_ContentMissingDetail", string.Join(", ", missing));
                Bundles.Clear();
                RecentAttempts.Clear();
                ResumableMock = null;
                return;
            }

            var bundles = await _mock.ListAvailableBundlesAsync();
            Bundles.Clear();
            foreach (var b in bundles) Bundles.Add(new MockBundleCard(b));
            SelectedBundle ??= Bundles.FirstOrDefault();

            ResumableMock = await _mock.FindResumableAsync();

            var history = await _mock.ListAttemptsAsync(30);
            RecentAttempts.Clear();
            foreach (var a in history) RecentAttempts.Add(a);

            if (Bundles.Count == 0)
                StatusText = Loc.Tr("Mock_NoBundles");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load mock hub");
            StatusText = Loc.Tr("Mock_LoadFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedBundleChanged(MockBundleCard? value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnSelectedModeChanged(MockMode value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnCurrentScreenChanged(object? value) => OnPropertyChanged(nameof(IsHubVisible));

    private bool CanStart() => SelectedMode == MockMode.RandomMix || SelectedBundle is not null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        try
        {
            MockBundleSummary? bundle = SelectedMode == MockMode.RandomMix
                ? await _mock.PickRandomBundleAsync()
                : SelectedBundle?.Bundle;

            if (bundle is null)
            {
                StatusText = Loc.Tr("Mock_NoBundleForStart");
                return;
            }

            int id = await _mock.StartAttemptAsync(SelectedMode, bundle);
            OpenSession(id);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start mock");
            StatusText = Loc.Tr("Mock_StartExamFailed") + ex.Message;
        }
    }

    [RelayCommand]
    private void Resume()
    {
        if (ResumableMock is not { } r) return;
        OpenSession(r.AttemptId);
    }

    [RelayCommand]
    private async Task DiscardResumableAsync()
    {
        if (ResumableMock is not { } r) return;

        var ok = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("Mock_DiscardTitle"),
            Loc.Tr("Mock_DiscardMessage"),
            confirmText: Loc.Tr("Mock_DiscardConfirm"));
        if (!ok) return;

        try
        {
            await _mock.DeleteAsync(r.AttemptId);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to discard resumable mock");
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenAttemptAsync(MockAttemptSummary? summary)
    {
        if (summary is null) return;
        await OpenResultAsync(summary.AttemptId);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var ok = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("Mock_ClearHistoryTitle"),
            Loc.Tr("Mock_ClearHistoryMessage"),
            confirmText: Loc.Tr("Mock_ClearHistoryConfirm"));
        if (!ok) return;

        try
        {
            var n = await _mock.ClearHistoryAsync();
            StatusText = n == 0 ? Loc.Tr("Mock_HistoryAlreadyEmpty") : Loc.Format("Mock_HistoryCleared", n);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to clear mock history");
            StatusText = Loc.Tr("Mock_ClearHistoryFailed") + ex.Message;
        }
        await LoadAsync();
    }

    private void OpenSession(int mockAttemptId)
    {
        var sessionVm = _services.GetRequiredService<MockSessionViewModel>();
        sessionVm.Closed += async finalisedId =>
        {
            // Финализированный экзамен → сразу в результат; иначе (выход/прерывание) — назад в Hub.
            if (finalisedId is int fid)
            {
                await OpenResultAsync(fid);
            }
            else
            {
                CurrentScreen = null;
                await LoadAsync();
            }
        };
        CurrentScreen = sessionVm;
        _ = sessionVm.StartAsync(mockAttemptId);
    }

    private async Task OpenResultAsync(int mockAttemptId)
    {
        var resultVm = _services.GetRequiredService<MockResultViewModel>();
        resultVm.Closed += async () =>
        {
            CurrentScreen = null;
            await LoadAsync();
        };
        await resultVm.LoadAsync(mockAttemptId);
        CurrentScreen = resultVm;
    }
}

/// <summary>Лёгкий wrapper над <see cref="MockBundleSummary"/> для отображения карточки.</summary>
public sealed record MockBundleCard(MockBundleSummary Bundle)
{
    public string Title => $"Cambridge {Bundle.Book} · Test {Bundle.TestNumber}";
    public string SectionsLabel => $"{Bundle.AvailableSections}/4";
    public bool IsFull => Bundle.AvailableSections >= 4;
}
