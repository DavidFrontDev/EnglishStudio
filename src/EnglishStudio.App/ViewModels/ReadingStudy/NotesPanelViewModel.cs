using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EnglishStudio.Modules.Reading.Services;
using Microsoft.Extensions.Logging;

namespace EnglishStudio.App.ViewModels.ReadingStudy;

/// <summary>
/// Notes panel (F5): lists a text's highlighted-span notes and hosts the add-note editor for a
/// freshly selected span. Built fresh per open (transient).
/// </summary>
public partial class NotesPanelViewModel : ObservableObject
{
    private readonly INotesService _service;
    private readonly ILogger<NotesPanelViewModel> _log;

    private int _textId;
    private int _pendingStart;
    private int _pendingLength;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasNotes;

    // Add-note editor.
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private string _pendingQuote = string.Empty;
    [ObservableProperty] private string _noteDraft = string.Empty;
    [ObservableProperty] private string _selectedColor = "#F4D03F";

    /// <summary>Highlight palette (hex).</summary>
    public IReadOnlyList<string> Colors { get; } = new[] { "#F4D03F", "#58D68D", "#85C1E9", "#F1948A", "#BB8FCE" };

    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();

    public event Action? CloseRequested;

    /// <summary>Raised after notes change so the reader can re-highlight.</summary>
    public event Action? NotesChanged;

    /// <summary>Raised when the user taps "перейти" on a note.</summary>
    public event Action<NoteDto>? NavigateRequested;

    public NotesPanelViewModel(INotesService service, ILogger<NotesPanelViewModel> log)
    {
        _service = service;
        _log = log;
    }

    public async Task InitializeAsync(int textId, CancellationToken ct = default)
    {
        _textId = textId;
        await ReloadAsync(ct);
    }

    private async Task ReloadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            var notes = await _service.ListNotesAsync(_textId, ct);
            Notes.Clear();
            foreach (var n in notes) Notes.Add(new NoteItemViewModel(n));
            HasNotes = Notes.Count > 0;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load notes for text {TextId}", _textId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Opens the add-note editor pre-filled with the selected span.</summary>
    public void BeginAdd(int startOffset, int length, string quote)
    {
        _pendingStart = startOffset;
        _pendingLength = length;
        PendingQuote = quote;
        NoteDraft = string.Empty;
        SelectedColor = Colors[0];
        IsAdding = true;
    }

    [RelayCommand]
    private void PickColor(string? color)
    {
        if (!string.IsNullOrEmpty(color)) SelectedColor = color;
    }

    [RelayCommand]
    private async Task SaveNote()
    {
        if (_pendingLength <= 0) { IsAdding = false; return; }
        try
        {
            await _service.AddNoteAsync(_textId, _pendingStart, _pendingLength, PendingQuote, NoteDraft.Trim(), SelectedColor);
            IsAdding = false;
            await ReloadAsync();
            NotesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to save note");
        }
    }

    [RelayCommand]
    private void CancelAdd() => IsAdding = false;

    [RelayCommand]
    private async Task DeleteNote(NoteItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            await _service.DeleteNoteAsync(item.Id);
            await ReloadAsync();
            NotesChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to delete note {Id}", item.Id);
        }
    }

    [RelayCommand]
    private void GoTo(NoteItemViewModel? item)
    {
        if (item is not null) NavigateRequested?.Invoke(item.Model);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}

/// <summary>One note in the panel.</summary>
public sealed class NoteItemViewModel
{
    public NoteDto Model { get; }

    public NoteItemViewModel(NoteDto model) => Model = model;

    public int Id => Model.Id;
    public string Quote => Model.Quote;
    public string NoteText => Model.NoteText;
    public string? Color => Model.Color;
    public bool HasText => !string.IsNullOrWhiteSpace(Model.NoteText);
}
