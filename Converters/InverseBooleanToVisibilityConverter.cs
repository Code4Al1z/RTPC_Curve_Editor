using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RTPCCurveEditor.Converters;

/// <summary>Returns Visible when the bound value is false, Collapsed when true. The inverse of the built-in BooleanToVisibilityConverter.</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}
