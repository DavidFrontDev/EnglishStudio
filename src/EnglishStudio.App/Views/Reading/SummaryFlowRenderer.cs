using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using EnglishStudio.App.ViewModels.Reading.Questions;

namespace EnglishStudio.App.Views.Reading;

/// <summary>
/// Attached property that binds an <see cref="IEnumerable"/> of <see cref="SummaryFlowSegment"/>
/// to a <see cref="TextBlock"/>'s <see cref="TextBlock.Inlines"/>, producing flowing prose with
/// inline numbered input boxes (proper IELTS summary-completion layout).
/// </summary>
public static class SummaryFlowRenderer
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments",
        typeof(IEnumerable),
        typeof(SummaryFlowRenderer),
        new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject element, IEnumerable? value)
        => element.SetValue(SegmentsProperty, value);

    public static IEnumerable? GetSegments(DependencyObject element)
        => (IEnumerable?)element.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IEnumerable items) return;

        foreach (var item in items)
        {
            if (item is not SummaryFlowSegment seg) continue;

            if (seg.IsText && seg.Text is not null)
            {
                tb.Inlines.Add(new Run(seg.Text));
            }
            else if (seg.IsGap && seg.GapQuestion is TextInputQuestionViewModel gapVm)
            {
                tb.Inlines.Add(BuildGap(gapVm));
            }
        }
    }

    private static InlineUIContainer BuildGap(TextInputQuestionViewModel vm)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };

        var number = new TextBlock
        {
            Text = $"{vm.DisplayNumber} ",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        number.SetResourceReference(TextBlock.ForegroundProperty, "AccentHotBrush");
        stack.Children.Add(number);

        var box = new TextBox
        {
            MinWidth = 110,
            Margin = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        box.SetResourceReference(Control.StyleProperty, "FlatTextBox");
        box.SetBinding(
            TextBox.TextProperty,
            new System.Windows.Data.Binding(nameof(TextInputQuestionViewModel.Text))
            {
                Source = vm,
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
            });
        stack.Children.Add(box);

        return new InlineUIContainer(stack) { BaselineAlignment = BaselineAlignment.Center };
    }
}
