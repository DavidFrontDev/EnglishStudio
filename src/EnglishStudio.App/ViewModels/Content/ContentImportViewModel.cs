using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Dictionary.Content;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace EnglishStudio.App.ViewModels.Content;

/// <summary>
/// Drives the content-import window: pick a pack (folder or .zip), preview its sections from the
/// manifest, run the import with a byte-fraction progress bar, then show a per-section summary and
/// any errors. Talks only to <see cref="IContentImportService"/> — backed by the local fake until
/// SP1, by the real service afterwards (no UI change). See plan §B1.
/// </summary>
public partial class ContentImportViewModel : ObservableObject
{
    private readonly IContentImportService _import;
    private readonly ILogger<ContentImportViewModel> _log;

    /// <summary>Raised when the user presses «Готово» so the hosting window can close itself.</summary>
    public event Action? CloseRequested;

    public ContentImportViewModel(IContentImportService import, ILogger<ContentImportViewModel> log)
    {
        _import = import;
        _log = log;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string? _selectedPath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseFolderCommand))]
    [NotifyCanExecuteChangedFor(nameof(ChooseZipCommand))]
    private bool _isImporting;

    [ObservableProperty] private double _progress;          // 0..1, bound to ProgressBar (Maximum=1)
    [ObservableProperty] private string _statusText = "Выберите папку или ZIP с контент-паком.";
    [ObservableProperty] private bool _isDone;

    /// <summary>Sections detected in the chosen pack's manifest (preview before import).</summary>
    public ObservableCollection<string> DetectedSections { get; } = [];

    /// <summary>Per-section result lines, populated after a finished import.</summary>
    public ObservableCollection<string> Summary { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;
    public bool HasDetectedSections => DetectedSections.Count > 0;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void ChooseFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку с контент-паком (содержит manifest.json)",
        };
        if (dlg.ShowDialog() == true)
            SetPath(dlg.FolderName);
    }

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void ChooseZip()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите ZIP с контент-паком",
            Filter = "Контент-пак (*.zip)|*.zip|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
            SetPath(dlg.FileName);
    }

    private bool CanChoose() => !IsImporting;

    private void SetPath(string path)
    {
        SelectedPath = path;
        IsDone = false;
        Summary.Clear();
        Errors.Clear();
        OnPropertyChanged(nameof(HasErrors));
        Progress = 0;
        PreviewManifest(path);
    }

    private void PreviewManifest(string path)
    {
        DetectedSections.Clear();
        try
        {
            var manifest = _import.PeekManifest(path);
            if (manifest is null)
            {
                StatusText = "В выбранном паке не найден manifest.json.";
            }
            else
            {
                foreach (var section in AllSections)
                    if (manifest.Has(section))
                        DetectedSections.Add(LabelOf(section));

                StatusText = DetectedSections.Count > 0
                    ? $"Готов к импорту: найдено секций — {DetectedSections.Count}."
                    : "Манифест найден, но в нём не отмечено ни одной секции.";
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PeekManifest failed for {Path}", path);
            StatusText = "Не удалось прочитать манифест пака: " + ex.Message;
        }
        OnPropertyChanged(nameof(HasDetectedSections));
    }

    private bool CanImport() => !string.IsNullOrWhiteSpace(SelectedPath) && !IsImporting;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath)) return;

        // Created on the UI thread → Progress<T> marshals the callback back to the UI thread for us.
        var progress = new Progress<ImportProgress>(p =>
        {
            Progress = p.Fraction;
            StatusText = string.IsNullOrEmpty(p.CurrentFile)
                ? p.Stage
                : $"{p.Stage} — {p.CurrentFile}";
        });

        IsImporting = true;
        IsDone = false;
        Summary.Clear();
        Errors.Clear();
        OnPropertyChanged(nameof(HasErrors));

        try
        {
            var result = await _import.ImportAsync(SelectedPath, progress);

            foreach (var s in result.Sections)
                Summary.Add($"{LabelOf(s.Section)} — {(s.Reseeded ? "обновлено" : "пропущено")} ({s.ItemCount})");
            foreach (var e in result.Errors)
                Errors.Add(e);

            Progress = 1;
            StatusText = result.Success
                ? "Импорт завершён успешно."
                : "Импорт завершён с ошибками — см. список ниже.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Content import failed for {Path}", SelectedPath);
            Errors.Add(ex.Message);
            StatusText = "Импорт не выполнен: " + ex.Message;
        }
        finally
        {
            IsImporting = false;
            IsDone = true;
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    [RelayCommand]
    private void Done() => CloseRequested?.Invoke();

    private static readonly ContentSection[] AllSections =
    [
        ContentSection.DictionaryOxford,
        ContentSection.DictionaryPhave,
        ContentSection.Reading,
        ContentSection.Listening,
        ContentSection.Writing,
        ContentSection.Speaking,
    ];

    private static string LabelOf(ContentSection section) => section switch
    {
        ContentSection.DictionaryOxford => "Словарь — Oxford 5000",
        ContentSection.DictionaryPhave  => "Словарь — фразовые глаголы (PHaVE)",
        ContentSection.Reading          => "Reading",
        ContentSection.Listening        => "Listening",
        ContentSection.Writing          => "Writing",
        ContentSection.Speaking         => "Speaking",
        _                               => section.ToString(),
    };
}
