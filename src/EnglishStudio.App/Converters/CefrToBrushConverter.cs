using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EnglishStudio.Modules.Dictionary.Entities;

namespace EnglishStudio.App.Converters;

public class CefrToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CefrLevel level) return Brushes.Gray;

        var key = level switch
        {
            CefrLevel.A1 => "PillA1Brush",
            CefrLevel.A2 => "PillA2Brush",
            CefrLevel.B1 => "PillB1Brush",
            CefrLevel.B2 => "PillB2Brush",
            CefrLevel.C1 => "PillC1Brush",
            CefrLevel.C2 => "PillC2Brush",
            _            => "PillUnknownBrush",
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
