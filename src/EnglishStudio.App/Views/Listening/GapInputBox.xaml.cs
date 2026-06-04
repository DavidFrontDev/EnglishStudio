using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Inline "fill-in-the-blank" field used by Anketa / Table / Comparison cards. When empty and
/// unfocused it shows a dotted leader with the question number in the middle (".... 10 ....").
/// The dots and number vanish as soon as the field gains focus, and reappear on blur if the
/// user typed nothing.
/// </summary>
public partial class GapInputBox : UserControl
{
    public static readonly DependencyProperty QuestionProperty = DependencyProperty.Register(
        nameof(Question),
        typeof(TextInputQuestionViewModel),
        typeof(GapInputBox),
        new PropertyMetadata(null, OnQuestionChanged));

    public TextInputQuestionViewModel? Question
    {
        get => (TextInputQuestionViewModel?)GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    public GapInputBox()
    {
        InitializeComponent();
        Input.GotKeyboardFocus += (_, _) => UpdatePlaceholder();
        Input.LostKeyboardFocus += (_, _) => UpdatePlaceholder();
        Input.TextChanged += (_, _) => UpdatePlaceholder();
        UpdatePlaceholder();
    }

    private static void OnQuestionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (GapInputBox)d;
        if (e.NewValue is TextInputQuestionViewModel vm)
        {
            box.NumberText.Text = vm.DisplayNumber.ToString();
            box.Input.SetBinding(TextBox.TextProperty, new Binding(nameof(TextInputQuestionViewModel.Text))
            {
                Source = vm,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        }
        else
        {
            BindingOperations.ClearBinding(box.Input, TextBox.TextProperty);
            box.NumberText.Text = string.Empty;
        }
        box.UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        var empty = string.IsNullOrEmpty(Input.Text);
        var focused = Input.IsKeyboardFocusWithin;
        Placeholder.Visibility = empty && !focused ? Visibility.Visible : Visibility.Collapsed;
    }
}
