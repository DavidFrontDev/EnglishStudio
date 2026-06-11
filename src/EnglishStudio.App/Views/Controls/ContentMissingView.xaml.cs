using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnglishStudio.App.Localization;

namespace EnglishStudio.App.Views.Controls;

/// <summary>
/// Reusable "this section's content isn't imported yet" banner. Each hub binds its own
/// <see cref="MessageText"/> (e.g. Mock lists the missing sections) and its own
/// <see cref="ImportCommand"/> (opens the importer). See plan §B2.
/// </summary>
public partial class ContentMissingView : UserControl
{
    private string _appliedDefault = string.Empty;

    public ContentMissingView()
    {
        InitializeComponent();
        // Apply the localized default only if the caller has not already set a value.
        if (string.IsNullOrEmpty(MessageText))
        {
            _appliedDefault = Loc.Tr("Controls_ContentMissingDefault");
            MessageText = _appliedDefault;
        }
        // Weak subscription: re-resolve the default on a language switch without leaking
        // repeatedly-created instances.
        PropertyChangedEventManager.AddHandler(
            LocalizationManager.Instance, OnLanguageChanged, string.Empty);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Only refresh the text if it is still our localized default (the caller hasn't overridden it).
        if (_appliedDefault.Length > 0 && MessageText == _appliedDefault)
        {
            _appliedDefault = Loc.Tr("Controls_ContentMissingDefault");
            MessageText = _appliedDefault;
        }
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
            new PropertyMetadata(string.Empty));

    public string MessageText
    {
        get => (string)GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }
}
