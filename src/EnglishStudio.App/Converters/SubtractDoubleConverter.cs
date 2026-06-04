using System.Globalization;
using System.Windows.Data;

namespace EnglishStudio.App.Converters;

/// <summary>
/// Returns <c>value - parameter</c> (both doubles), clamped to ≥ 0. Used to derive a child's
/// MaxWidth from an ancestor's ViewportWidth minus a fixed inset (card padding + number column),
/// so question blocks shrink to their content yet never exceed the available width.
/// </summary>
public sealed class SubtractDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double v || double.IsNaN(v) || double.IsInfinity(v)) return double.PositiveInfinity;
        var inset = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            _ => 0d
        };
        return Math.Max(0d, v - inset);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
