using System.Windows;
using System.Windows.Threading;

namespace RTPCCurveEditor;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe app will try to continue running.",
            "RTPC Curve Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}