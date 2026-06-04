using System.Windows;
using System.Windows.Media;

namespace EnglishStudio.App.Views.Reading;

/// <summary>
/// Attached property that tags a question card with its display number so the host view can
/// locate it via visual-tree traversal and <c>BringIntoView</c>.
/// </summary>
public static class QuestionAnchor
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.RegisterAttached(
        "Number",
        typeof(int),
        typeof(QuestionAnchor),
        new PropertyMetadata(0));

    public static void SetNumber(DependencyObject element, int value) => element.SetValue(NumberProperty, value);
    public static int GetNumber(DependencyObject element) => (int)element.GetValue(NumberProperty);

    /// <summary>Recursively searches <paramref name="root"/> for a FrameworkElement whose anchor number matches.</summary>
    public static FrameworkElement? Find(DependencyObject root, int number)
    {
        if (root is FrameworkElement fe && GetNumber(fe) == number) return fe;

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = Find(VisualTreeHelper.GetChild(root, i), number);
            if (found is not null) return found;
        }
        return null;
    }
}
