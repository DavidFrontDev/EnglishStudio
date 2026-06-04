using System.Windows;
using EnglishStudio.App.Shell;

namespace EnglishStudio.App.Views.Dialogs;

/// <summary>
/// Тематический диалог подтверждения (Yes/No) в стиле приложения (ChromedWindow), замена
/// системного MessageBox. Используй <see cref="Show"/>.
/// </summary>
public partial class ConfirmWindow : ChromedWindow
{
    public ConfirmWindow()
    {
        InitializeComponent();
    }

    private ConfirmWindow(string title, string message, string confirmText, string cancelText, string icon)
        : this()
    {
        Title = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
        IconText.Text = icon;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Показывает модальный диалог подтверждения; true — пользователь подтвердил.</summary>
    public static bool Show(
        Window? owner,
        string title,
        string message,
        string confirmText = "Да",
        string cancelText = "Отмена",
        string icon = "⚠")
    {
        var dialog = new ConfirmWindow(title, message, confirmText, cancelText, icon);
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
