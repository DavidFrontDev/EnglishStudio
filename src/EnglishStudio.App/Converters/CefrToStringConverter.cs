using System.Globalization;
using System.Windows.Data;
using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.Converters;

public class CefrToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CefrLevel level && level != CefrLevel.Unknown ? level.ToString() : "—";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
