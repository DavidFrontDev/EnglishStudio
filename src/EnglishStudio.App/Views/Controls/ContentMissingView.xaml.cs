using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace EnglishStudio.App.Views.Controls;

/// <summary>
/// Reusable "this section's content isn't imported yet" banner. Each hub binds its own
/// <see cref="MessageText"/> (e.g. Mock lists the missing sections) and its own
/// <see cref="ImportCommand"/> (opens the importer). See plan §B2.
/// </summary>
public partial class ContentMissingView : UserControl
{
    public ContentMissingView()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ImportCommandProperty =
        DependencyProperty.Register(
            nameof(ImportCommand),
            typeof(ICommand),
            typeof(ContentMissingView),
            new PropertyMetadata(null));

    public ICommand? ImportCommand
    {
        get => (ICommand?)GetValue(ImportCommandProperty);
        set => SetValue(ImportCommandProperty, value);
    }

    public static readonly DependencyProperty MessageTextProperty =
        DependencyProperty.Register(
            nameof(MessageText),
            typeof(string),
            typeof(ContentMissingView),
            new PropertyMetadata(
                "Скачайте или соберите контент-пак и импортируйте его, чтобы открыть этот раздел."));

    public string MessageText
    {
        get => (string)GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }
}
