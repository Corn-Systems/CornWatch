using System.Security.Principal;
using System.Text;

namespace CornWatch;

// Persistent per-day log at %AppData%\CornSystems\CornWatch\logs\yyyy-MM-dd.log.
// Every swallowed exception in the codebase reports here instead of vanishing
// into an empty catch {}.
internal static class sessionLog
{
    private const int retentionDays = 14;
    private static readonly object gate = new();
    private static bool pruned;

    public static void write(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            var now = DateTime.Now;
            lock (gate)
            {
                appPaths.ensureDir(appPaths.logsDir);
                if (!pruned) { prune(); pruned = true; }
                File.AppendAllText(Path.Combine(appPaths.logsDir, $"{now:yyyy-MM-dd}.log"),
                    $"[{now:HH:mm:ss}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* logging must never take the app down */ }
    }

    public static void write(string tag, Exception ex) =>
        write($"[{tag}] {ex.GetType().Name}: {ex.Message}");

    // Call once at startup to stamp each session in the log file.
    public static void sessionHeader() =>
        write($"===== {appInfo.name} {appInfo.version} started — user={Environment.UserName}, " +
              $"elevated={isElevated()}, os={Environment.OSVersion.Version} =====");

    private static bool isElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static void prune()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-retentionDays);
            foreach (var f in Directory.GetFiles(appPaths.logsDir, "*.log").Where(p => File.GetLastWriteTime(p) < cutoff))
                File.Delete(f);
        }
        catch { /* prune is opportunistic */ }
    }
}
