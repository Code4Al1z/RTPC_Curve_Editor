using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RTPCCurveEditor.Models;
using RTPCCurveEditor.ViewModels;

namespace RTPCCurveEditor.Views;

public partial class InspectorPanel : UserControl
{
    public InspectorPanel() => InitializeComponent();

    // The CheckBox and "✕" button inside each row handle their own clicks
    // (Handled = true internally), so this only fires for clicks on the row
    // itself — clicking a curve in the list now activates it, matching what
    // clicking a curve on the canvas already does.
    private void OnCurveRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BezierCurve curve } &&
            DataContext is MainViewModel vm)
        {
            vm.SetActiveCurveCommand.Execute(curve);
        }
    }
}