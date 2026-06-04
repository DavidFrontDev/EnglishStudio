using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.Views.Listening;

/// <summary>
/// Inline letter-picker used by CascadeImage cards. Replaces the dotted gap with a compact
/// <see cref="ComboBox"/> whose items are the shared A–H tags. Shows the question number as a
/// centred placeholder until the user makes a choice.
/// </summary>
public partial class GapComboBox : UserControl
{
    public static readonly DependencyProperty QuestionProperty = DependencyProperty.Register(
        nameof(Question),
        typeof(MatchingQuestionViewModel),
        typeof(GapComboBox),
        new PropertyMetadata(null, OnQuestionChanged));

    public MatchingQuestionViewModel? Question
    {
        get => (MatchingQuestionViewModel?)GetValue(QuestionProperty);
        set => SetValue(QuestionProperty, value);
    }

    public GapComboBox()
    {
        InitializeComponent();
        Picker.SelectionChanged += (_, _) => UpdatePlaceholder();
        UpdatePlaceholder();
    }

    private static void OnQuestionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (GapComboBox)d;
        if (e.NewValue is MatchingQuestionViewModel vm)
        {
            box.NumberText.Text = vm.DisplayNumber.ToString();
            box.Picker.ItemsSource = vm.AvailableTags;
            box.Picker.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(MatchingQuestionViewModel.SelectedTag))
            {
                Source = vm,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        }
        else
        {
            BindingOperations.ClearBinding(box.Picker, Selector.SelectedItemProperty);
            box.Picker.ItemsSource = null;
            box.NumberText.Text = string.Empty;
        }
        box.UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
        => NumberText.Visibility = Picker.SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;
}
