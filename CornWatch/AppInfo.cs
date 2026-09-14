using System.Reflection;

namespace CornWatch;

// Identity strings live here so they aren't scattered as literals across the UI,
// the User-Agent, the update checker, and the log files.
internal static class AppInfo
{
    public const string Name        = "CornWatch";
    public const string Publisher   = "Corn Systems";
    public const string RepoOwner   = "Corn-Systems";
    public const string RepoName    = "CornWatch";
    public const string RepoUrl     = "https://github.com/" + RepoOwner + "/" + RepoName;
    public const string ReleasesUrl = RepoUrl + "/releases";

    private static string _version = null;

    // Read from <Version> in the .csproj (via AssemblyInformationalVersion),
    // stripped of any "+commit" suffix SourceLink might append.
    public static string Version
    {
        get
        {
            if (_version is not null) return _version;
            try
            {
                var asm  = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrWhiteSpace(info))
                    info = asm.GetName().Version?.ToString(3);
                int plus = info?.IndexOf('+') ?? -1;
                _version = plus > 0 ? info[..plus] : (info ?? "0.0.0");
            }
            catch { _version = "0.0.0"; }
            return _version;
        }
    }

    public static string UserAgent => $"CornWatch/{Version} (+{RepoUrl})";
}
