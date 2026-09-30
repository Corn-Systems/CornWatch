using System.Reflection;

namespace CornWatch;

// Identity strings live here so they aren't scattered as literals across the UI,
// the User-Agent, the update checker, and the log files.
internal static class appInfo
{
    public const string name = "CornWatch";
    public const string repoOwner = "Corn-Systems";
    public const string repoName = "CornWatch";
    public const string repoUrl = "https://github.com/" + repoOwner + "/" + repoName;
    public const string releasesUrl = repoUrl + "/releases";

    public static string version { get; } = readVersion();

    public static string userAgent => $"{name}/{version} (+{repoUrl})";

    // Read from <Version> in the .csproj (via AssemblyInformationalVersion),
    // stripped of any "+commit" suffix SourceLink might append.
    private static string readVersion()
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
}
