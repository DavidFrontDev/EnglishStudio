using System.Globalization;
using System.Windows.Data;

namespace EnglishStudio.App.Converters;

/// <summary>
/// Two-way converter for binding an enum-typed source property to a RadioButton's
/// IsChecked. Pass the target enum name as ConverterParameter; the source enum's
/// value is compared by name (case-insensitive).
/// </summary>
public sealed class EnumBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string name) return false;
        return string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string name && targetType.IsEnum)
        {
            return Enum.Parse(targetType, name, ignoreCase: true);
        }
        return Binding.DoNothing;
    }
}
