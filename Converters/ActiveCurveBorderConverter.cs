using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RTPCCurveEditor.Models;

namespace RTPCCurveEditor.Converters;

/// <summary>
/// MultiBinding converter for the comparison-curve list: values[0] is the row's
/// curve item, values[1] is the current ActiveCurve. Returns an accent brush
/// when they're the same curve (by reference), a neutral one otherwise.
/// ConverterParameter "background" returns a faint tint suitable for a fill;
/// anything else returns a solid brush suitable for a border.
/// </summary>
public class ActiveCurveBorderConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0x7F, 0x77, 0xDD));
    private static readonly SolidColorBrush AccentFaint = new(Color.FromArgb(30, 0x7F, 0x77, 0xDD));
    private static readonly SolidColorBrush NeutralBorder = new(Color.FromRgb(0x2A, 0x2A, 0x38));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool isActive = values.Length == 2
            && values[0] is BezierCurve item
            && values[1] is BezierCurve active
            && ReferenceEquals(item, active);

        if (parameter as string == "background")
            return isActive ? AccentFaint : Brushes.Transparent;

        return isActive ? Accent : NeutralBorder;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
