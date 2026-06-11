using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Views.Dialogs;
using EnglishStudio.App.Views.ReadingStudy;
using EnglishStudio.Modules.Dictionary.Entities;
using EnglishStudio.Modules.Reading;
using EnglishStudio.Modules.Reading.Entities;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Study "Чтение" hub + router. Lists the user's texts, hosts the add-text flow,
/// and swaps to the reader screen via <see cref="CurrentScreen"/>.
/// </summary>
public partial class ReadingLibraryViewModel : ObservableObject
{
    private readonly ITextLibraryService _library;
    private readonly IServiceProvider _services;
    private readonly ILogger<ReadingLibraryViewModel> _log;

    private readonly List<ReadingTextListItem> _all = new();

    public ObservableCollection<ReadingTextListItem> Texts { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = string.Empty;

    // ── F7 filters: stable language-neutral tokens + localized labels, rebuilt live on switch ──
    /// <summary>A filter ComboBox item: stable <see cref="Token"/> for comparison + localized <see cref="Label"/>.</summary>
    public sealed record FilterOption(string Token, string Label);

    public ObservableCollection<FilterOption> SourceOptions { get; } = new();
    public ObservableCollection<FilterOption> CefrOptions { get; } = new();

    [ObservableProperty] private string _selectedSource = "all";
    [ObservableProperty] private string _selectedCefr = "all";

    /// <summary>When off (default), soft-hidden texts are filtered out of the library.</summary>
    [ObservableProperty] private bool _showHidden;

    // ── F6 progress overlay ────────────────────────────────────────────────
    [ObservableProperty] private ReadingProgressViewModel? _progress;
    [ObservableProperty] private bool _isProgressVisible;

    partial void OnSelectedSourceChanged(string value) => ApplyFilter();
    partial void OnSelectedCefrChanged(string value) => ApplyFilter();
    partial void OnShowHiddenChanged(bool value) => ApplyFilter();

    public ReadingLibraryViewModel(
        ITextLibraryService library,
        IServiceProvider services,
        ILogger<ReadingLibraryViewModel> log)
    {
        _library = library;
        _services = services;
        _log = log;

        RebuildFilterLabels();
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;

        _ = LoadAsync();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => RebuildFilterLabels();

    /// <summary>(Re)builds the filter options with localized labels, preserving the selected tokens.</summary>
    private void RebuildFilterLabels()
    {
        var src = SelectedSource;
        var cefr = SelectedCefr;
        SourceOptions.Clear();
        SourceOptions.Add(new FilterOption("all", Loc.Tr("ReadStudy_SourceAll")));
        SourceOptions.Add(new FilterOption("mine", Loc.Tr("ReadStudy_SourceMine")));
        SourceOptions.Add(new FilterOption("builtin", Loc.Tr("ReadStudy_SourceBuiltin")));
        CefrOptions.Clear();
        CefrOptions.Add(new FilterOption("all", Loc.Tr("ReadStudy_CefrAll")));
        foreach (var lvl in new[] { "A1", "A2", "B1", "B2", "C1", "C2" })
            CefrOptions.Add(new FilterOption(lvl, lvl));
        SelectedSource = src;
        SelectedCefr = cefr;
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedCefr));
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = string.Empty;
        try
        {
            var items = await _library.ListAsync();
            _all.Clear();
            _all.AddRange(items);
            ApplyFilter();

            if (_all.Count == 0)
                StatusText = Loc.Tr("ReadStudy_NoTextsYet");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load reading library");
            StatusText = Loc.Tr("ReadStudy_LoadTextsFailed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Rebuilds <see cref="Texts"/> from <see cref="_all"/> using the source / CEFR filters.</summary>
    private void ApplyFilter()
    {
        IEnumerable<ReadingTextListItem> q = _all;

        q = SelectedSource switch
        {
            "mine" => q.Where(t => t.Source != ReadingSource.Builtin),
            "builtin" => q.Where(t => t.Source == ReadingSource.Builtin),
            _ => q,
        };

        if (!ShowHidden)
            q = q.Where(t => !t.IsHidden);

        if (SelectedCefr != "all" && Enum.TryParse<CefrLevel>(SelectedCefr, out var level))
            q = q.Where(t => t.EstimatedCefr == level);

        Texts.Clear();
        foreach (var it in q) Texts.Add(it);

        if (_all.Count > 0 && Texts.Count == 0)
            StatusText = Loc.Tr("ReadStudy_NoTextsFilter");
        else if (_all.Count > 0)
            StatusText = string.Empty;
    }

    [RelayCommand]
    private async Task ShowProgress()
    {
        var vm = _services.GetService<ReadingProgressViewModel>();
        if (vm is null) return;

        vm.CloseRequested -= OnProgressClosed;
        vm.CloseRequested += OnProgressClosed;
        Progress = vm;
        IsProgressVisible = true;
        await vm.InitializeAsync();
    }

    private void OnProgressClosed() => IsProgressVisible = false;

    [RelayCommand]
    private async Task AddText()
    {
        var result = AddTextWindow.Show(Application.Current.MainWindow);
        if (result is null) return;

        var words = ReadingTokenizer.CountWords(result.Body);
        if (words > ReadingTokenizer.LargeTextWordThreshold)
        {
            var proceed = ConfirmWindow.Show(
                Application.Current.MainWindow,
                Loc.Tr("ReadStudy_LargeTextTitle"),
                Loc.Format("ReadStudy_LargeTextBody", words, ReadingTokenizer.LargeTextWordThreshold),
                confirmText: Loc.Tr("ReadStudy_Add"),
                cancelText: Loc.Tr("ReadStudy_Cancel"),
                icon: "⚠");
            if (!proceed) return;
        }

        try
        {
            await _library.AddAsync(result.Title, result.Body, ReadingSource.User);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to add reading text");
            StatusText = Loc.Tr("ReadStudy_SaveTextFailed");
        }
    }

    [RelayCommand]
    private Task Open(ReadingTextListItem? item) => OpenReaderAsync(item, showPreTeach: false);

    [RelayCommand]
    private Task Prepare(ReadingTextListItem? item) => OpenReaderAsync(item, showPreTeach: true);

    private async Task OpenReaderAsync(ReadingTextListItem? item, bool showPreTeach)
    {
        if (item is null) return;

        var reader = _services.GetRequiredService<ReaderViewModel>();
        await reader.LoadAsync(item.Id);

        var window = new ReaderWindow
        {
            DataContext = reader,
            Owner = Application.Current.MainWindow
        };
        reader.CloseRequested += window.Close;
        // Refresh ordering (LastOpenedAt) when the reader is closed.
        window.Closed += (_, _) => _ = LoadAsync();
        window.Show();

        if (showPreTeach)
            reader.ShowPreTeachCommand.Execute(null);
    }

    [RelayCommand]
    private async Task Rename(ReadingTextListItem? item)
    {
        if (item is null) return;

        var newTitle = RenameWindow.Show(
            Application.Current.MainWindow,
            item.Title,
            title: Loc.Tr("ReadStudy_RenameTextTitle"),
            caption: Loc.Tr("ReadStudy_RenameTextCaption"));
        if (newTitle is null) return;

        try
        {
            await _library.RenameAsync(item.Id, newTitle);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to rename reading text {Id}", item.Id);
            StatusText = Loc.Tr("ReadStudy_RenameTextFailed");
        }
    }

    [RelayCommand]
    private async Task Delete(ReadingTextListItem? item)
    {
        if (item is null) return;

        var ok = ConfirmWindow.Show(
            Application.Current.MainWindow,
            Loc.Tr("ReadStudy_DeleteTextTitle"),
            Loc.Format("ReadStudy_DeleteTextBody", item.Title),
            confirmText: Loc.Tr("ReadStudy_Delete"),
            icon: "🗑");
        if (!ok) return;

        try
        {
            await _library.DeleteAsync(item.Id);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete reading text {Id}", item.Id);
            StatusText = Loc.Tr("ReadStudy_DeleteTextFailed");
        }
    }

    /// <summary>Soft-hide a (typically built-in) text so it drops out of the library.</summary>
    [RelayCommand]
    private Task Hide(ReadingTextListItem? item) => SetHiddenAsync(item, hidden: true);

    /// <summary>Restore a previously hidden text (visible only with "show hidden" on).</summary>
    [RelayCommand]
    private Task Restore(ReadingTextListItem? item) => SetHiddenAsync(item, hidden: false);

    private async Task SetHiddenAsync(ReadingTextListItem? item, bool hidden)
    {
        if (item is null) return;
        try
        {
            await _library.SetHiddenAsync(item.Id, hidden);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to set hidden={Hidden} for reading text {Id}", hidden, item.Id);
            StatusText = Loc.Tr("ReadStudy_SetHiddenFailed");
        }
    }
}
