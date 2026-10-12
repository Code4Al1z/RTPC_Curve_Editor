using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace RTPCCurveEditor.Services;

public static class BugReportService
{
    public const string Address = "trailblaiz@trailblaiz.studio";

    private const int MaxMailtoLength = 1900;

    public static bool Send(string appDiagnostics, string? errorDetails = null)
    {
        string fullBody = BuildBody(appDiagnostics, errorDetails, 4000);
        string subject = $"[RTPC Curve Editor v{AppInfo.Version}] Bug report";

        try { Clipboard.SetText(fullBody); } catch { }

        string url = BuildMailto(subject, BuildBody(appDiagnostics, errorDetails, 400));
        if (url.Length > MaxMailtoLength)
            url = BuildMailto(subject, BuildBody(appDiagnostics, null, 0));

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            MessageBox.Show(
                $"Couldn't open an email app.\n\nThe bug report has been copied to your clipboard. Please paste it into an email to {Address}.",
                "Report a bug",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }
    }

    private static string BuildMailto(string subject, string body) =>
        $"mailto:{Address}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

    private static string BuildBody(string appDiagnostics, string? errorDetails, int maxErrorLength)
    {
        var sb = new StringBuilder();
        sb.AppendLine("What happened:");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("What I expected:");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Steps to reproduce:");
        sb.AppendLine("1. ");
        sb.AppendLine();
        sb.AppendLine("--- Diagnostics (auto-generated, safe to leave in) ---");
        sb.AppendLine($"App version: {AppInfo.Version}");
        sb.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine(appDiagnostics.TrimEnd());

        if (!string.IsNullOrWhiteSpace(errorDetails) && maxErrorLength > 0)
        {
            string trimmed = errorDetails.Length > maxErrorLength
                ? errorDetails[..maxErrorLength] + "..."
                : errorDetails;
            sb.AppendLine();
            sb.AppendLine("Error:");
            sb.AppendLine(trimmed);
        }

        return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
    }
}
