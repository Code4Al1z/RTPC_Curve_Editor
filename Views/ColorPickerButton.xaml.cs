using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RTPCCurveEditor.Views;

/// <summary>
/// A small swatch button that opens a popup with preset colours and RGB
/// sliders. Binds two-way to <see cref="ColorHex"/> (a "#RRGGBB" string),
/// the same representation <c>BezierCurve.ColorHex</c> already uses
/// everywhere else, so this drops in next to the existing hex textbox
/// without needing any conversion at the binding site.
/// </summary>
public partial class ColorPickerButton : UserControl
{
    public static readonly string[] PresetColors =
    {
        "#7F77DD", "#1D9E75", "#D4537E", "#EF9F27",
        "#3B82F6", "#10B981", "#F43F5E", "#F59E0B",
        "#A855F7", "#06B6D4", "#EAB308", "#FFFFFF"
    };

    public static readonly DependencyProperty ColorHexProperty =
        DependencyProperty.Register(
            nameof(ColorHex),
            typeof(string),
            typeof(ColorPickerButton),
            new FrameworkPropertyMetadata("#7F77DD", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorHexChanged));

    public string ColorHex
    {
        get => (string)GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    public static readonly DependencyProperty SwatchBrushProperty =
        DependencyProperty.Register(nameof(SwatchBrush), typeof(Brush), typeof(ColorPickerButton),
            new PropertyMetadata(Brushes.Gray));

    public Brush SwatchBrush
    {
        get => (Brush)GetValue(SwatchBrushProperty);
        private set => SetValue(SwatchBrushProperty, value);
    }

    // Guards against the slider ValueChanged handlers re-writing ColorHex
    // (and re-triggering OnColorHexChanged) while we're the ones updating
    // the sliders' Value from an externally-set ColorHex.
    private bool _syncingFromHex;

    public ColorPickerButton()
    {
        InitializeComponent();
        UpdateFromHex(ColorHex);
    }

    private static void OnColorHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerButton picker)
            picker.UpdateFromHex(e.NewValue as string);
    }

    private void UpdateFromHex(string? hex)
    {
        if (!TryParseHex(hex, out var color)) return;

        SwatchBrush = new SolidColorBrush(color);

        _syncingFromHex = true;
        RSlider.Value = color.R;
        GSlider.Value = color.G;
        BSlider.Value = color.B;
        _syncingFromHex = false;
    }

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.Gray;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
            // Invalid/partial hex (e.g. mid-typing in the paired textbox) — keep
            // whatever the picker last showed rather than throwing or blanking.
        }
        return false;
    }

    private void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingFromHex) return;

        var color = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        SwatchBrush = new SolidColorBrush(color);
        ColorHex = hex;
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex })
        {
            ColorHex = hex;
            PickerPopup.IsOpen = false;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        PickerPopup.IsOpen = false;
    }
}
