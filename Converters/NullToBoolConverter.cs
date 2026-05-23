using System.Globalization;
using System.Windows.Data;

namespace RTPCCurveEditor.Converters;

/// <summary>Returns true when the value is non-null. Used to enable/disable fields.</summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
