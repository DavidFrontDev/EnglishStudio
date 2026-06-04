using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace EnglishStudio.App.Converters;

/// <summary>
/// Converts a hex colour string (e.g. "#F4D03F") to a frozen <see cref="SolidColorBrush"/>.
/// Returns <see cref="Brushes.Transparent"/> for null/blank/invalid input. Used for note
/// highlight swatches and colour dots (F5).
/// </summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(s)!;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                // fall through to transparent
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
