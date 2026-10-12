using System.Reflection;

namespace RTPCCurveEditor.Services;

public static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return "0.0.0";
        int plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
