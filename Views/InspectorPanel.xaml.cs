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

    // The range TextBoxes use UpdateSourceTrigger=LostFocus (needed so the
    // confirm-first dialog fires once per edit, not once per keystroke — see
    // MainViewModel.RequestRangeChange). That means Enter alone doesn't commit
    // anything unless focus actually moves away. UpdateSource() commits the
    // binding directly, so Enter works without faking a focus change.
    private void OnRangeFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox tb)
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }
}