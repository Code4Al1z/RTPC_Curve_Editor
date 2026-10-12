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
        var answer = MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe app will try to continue running.\n\nWould you like to send a bug report?",
            "RTPC Curve Editor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Error);
        e.Handled = true;

        if (answer != MessageBoxResult.Yes) return;

        string diagnostics;
        try { diagnostics = (MainWindow?.DataContext as ViewModels.MainViewModel)?.BuildDiagnostics() ?? ""; }
        catch { diagnostics = ""; }

        Services.BugReportService.Send(diagnostics, e.Exception.ToString());
    }
}