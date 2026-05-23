using System.Windows;
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

        // Delete key removes selected point
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                VM.DeleteSelectedPoint();
                CurveCanvas.Redraw();
            }
        };
    }
}
