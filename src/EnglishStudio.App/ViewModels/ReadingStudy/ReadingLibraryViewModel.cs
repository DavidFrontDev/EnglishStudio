using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    // ── F7 filters ─────────────────────────────────────────────────────────
    public IReadOnlyList<string> SourceOptions { get; } = new[] { "Все тексты", "Мои", "Встроенные" };
    public IReadOnlyList<string> CefrOptions { get; } = new[] { "Все уровни", "A1", "A2", "B1", "B2", "C1", "C2" };

    [ObservableProperty] private string _selectedSource = "Все тексты";
    [ObservableProperty] private string _selectedCefr = "Все уровни";

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

        _ = LoadAsync();
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
                StatusText = "Пока нет текстов. Нажмите «Добавить текст», чтобы вставить свой.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load reading library");
            StatusText = "Не удалось загрузить список текстов.";
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
            "Мои" => q.Where(t => t.Source != ReadingSource.Builtin),
            "Встроенные" => q.Where(t => t.Source == ReadingSource.Builtin),
            _ => q,
        };

        if (!ShowHidden)
            q = q.Where(t => !t.IsHidden);

        if (SelectedCefr != "Все уровни" && Enum.TryParse<CefrLevel>(SelectedCefr, out var level))
            q = q.Where(t => t.EstimatedCefr == level);

        Texts.Clear();
        foreach (var it in q) Texts.Add(it);

        if (_all.Count > 0 && Texts.Count == 0)
            StatusText = "Нет текстов под выбранный фильтр.";
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
                "Очень большой текст",
                $"В тексте ~{words:N0} слов. Раздел «Чтение» рассчитан на рассказы и главы — " +
                $"тексты больше {ReadingTokenizer.LargeTextWordThreshold:N0} слов (например, целые книги) " +
                "могут открываться очень медленно или подвесить окно чтения. Всё равно добавить?",
                confirmText: "Добавить",
                cancelText: "Отмена",
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
            StatusText = "Не удалось сохранить текст.";
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
            title: "Переименовать текст",
            caption: "Название текста");
        if (newTitle is null) return;

        try
        {
            await _library.RenameAsync(item.Id, newTitle);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to rename reading text {Id}", item.Id);
            StatusText = "Не удалось переименовать текст.";
        }
    }

    [RelayCommand]
    private async Task Delete(ReadingTextListItem? item)
    {
        if (item is null) return;

        var ok = ConfirmWindow.Show(
            Application.Current.MainWindow,
            "Удалить текст",
            $"Удалить «{item.Title}»? Это действие необратимо.",
            confirmText: "Удалить",
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
            StatusText = "Не удалось удалить текст.";
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
            StatusText = "Не удалось изменить видимость текста.";
        }
    }
}
