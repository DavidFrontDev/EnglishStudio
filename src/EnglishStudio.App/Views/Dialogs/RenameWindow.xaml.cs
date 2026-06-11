using System.Windows;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Shell;

namespace EnglishStudio.App.Views.Dialogs;

/// <summary>
/// Themed single-field input dialog (ChromedWindow). Used for renaming reading texts.
/// Use <see cref="Show"/>.
/// </summary>
public partial class RenameWindow : ChromedWindow
{
    public RenameWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();
        InputBox.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(InputBox.Text)) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>
    /// Shows the dialog modally, prefilled with <paramref name="currentValue"/>.
    /// Returns the trimmed new value, or null if cancelled / unchanged / empty.
    /// </summary>
    public static string? Show(Window? owner, string currentValue, string? title = null, string? caption = null)
    {
        var dialog = new RenameWindow { Title = title ?? Loc.Tr("Dialog_RenameTitle") };
        dialog.CaptionText.Text = caption ?? Loc.Tr("Dialog_RenameCaption");
        dialog.InputBox.Text = currentValue;
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        if (dialog.ShowDialog() != true) return null;

        var value = dialog.InputBox.Text?.Trim() ?? string.Empty;
        if (value.Length == 0 || value == currentValue?.Trim()) return null;
        return value;
    }
}
