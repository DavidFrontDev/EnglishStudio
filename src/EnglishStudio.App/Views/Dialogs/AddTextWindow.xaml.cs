using System.IO;
using System.Windows;
using EnglishStudio.App.Localization;
using EnglishStudio.App.Shell;

namespace EnglishStudio.App.Views.Dialogs;

/// <summary>Result of the add-text dialog.</summary>
public sealed record AddTextResult(string Title, string Body);

/// <summary>
/// Themed dialog (ChromedWindow) for adding a reading text — paste a title + body,
/// or import a .txt file. Use <see cref="Show"/>.
/// </summary>
public partial class AddTextWindow : ChromedWindow
{
    public AddTextWindow()
    {
        InitializeComponent();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.Tr("Dialog_ImportTextTitle"),
            Filter = Loc.Tr("Dialog_ImportTextFilter"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            BodyBox.Text = File.ReadAllText(dlg.FileName);
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
                TitleBox.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
            HideError();
        }
        catch (Exception ex)
        {
            ShowError(Loc.Format("Dialog_ReadFileFailed", ex.Message));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BodyBox.Text))
        {
            ShowError(Loc.Tr("Dialog_PasteOrImportText"));
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    /// <summary>Shows the dialog modally; returns the entered text or null if cancelled.</summary>
    public static AddTextResult? Show(Window? owner)
    {
        var dialog = new AddTextWindow();
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        if (dialog.ShowDialog() != true) return null;

        return new AddTextResult(
            dialog.TitleBox.Text?.Trim() ?? string.Empty,
            dialog.BodyBox.Text ?? string.Empty);
    }
}
