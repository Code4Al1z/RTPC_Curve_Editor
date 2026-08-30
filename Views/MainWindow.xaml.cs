using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RTPCCurveEditor.ViewModels;

namespace RTPCCurveEditor.Views;

public partial class MainWindow : Window
{
    private MainViewModel VM => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // Wire canvas to ViewModel events so the canvas redraws on any model change
        VM.CurveChanged += () => CurveCanvas.Redraw();

        Closing += (_, e) =>
        {
            if (!VM.ConfirmDiscard()) e.Cancel = true;
        };

        // Delete key removes selected point (unless user is typing in a TextBox)
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                // Prevent deleting points while editing text inputs
                if (Keyboard.FocusedElement is TextBox) return;

                VM.DeleteSelectedPoint();
                CurveCanvas.Redraw();
            }
        };
    }
}