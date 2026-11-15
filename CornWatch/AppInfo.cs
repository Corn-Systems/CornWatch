using System.Reflection;
using System.Security.Principal;

namespace CornWatch;

// Identity strings live here so they aren't scattered as literals across the UI,
// the User-Agent, the update checker, and the log files.
internal static class AppInfo
{
    public const string Name = "CornWatch";
    public const string RepoOwner = "Corn-Systems";
    public const string RepoName = "CornWatch";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    public const string ReleasesUrl = RepoUrl + "/releases";

    public static string Version { get; } = ReadVersion();

    public static bool IsElevated { get; } = ReadElevated();

    public static string UserAgent => $"{Name}/{Version} (+{RepoUrl})";

    // Read from <Version> in the .csproj (via AssemblyInformationalVersion),
    // stripped of any "+commit" suffix SourceLink might append.
    private static string ReadVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(info)) info = asm.GetName().Version?.ToString(3);
            var plus = info?.IndexOf('+') ?? -1;
            return plus > 0 ? info![..plus] : info ?? "0.0.0";
        }
        catch { return "0.0.0"; }
    }

    private static bool ReadElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
