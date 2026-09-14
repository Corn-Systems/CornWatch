using System.Text;

namespace CornWatch;

// Persistent per-day log at %AppData%\CornSystems\CornWatch\logs\yyyy-MM-dd.log.
// Every swallowed exception in the codebase reports here instead of vanishing
// into an empty catch {}.
internal static class SessionLog
{
    private static readonly object _lock = new();
    private const int RetentionDays = 14;
    private static bool _pruned;

    public static string CurrentFile =>
        Path.Combine(AppPaths.LogsDir, $"{DateTime.Now:yyyy-MM-dd}.log");

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            lock (_lock)
            {
                AppPaths.EnsureLogsDir();
                if (!_pruned) { Prune(); _pruned = true; }
                File.AppendAllText(CurrentFile,
                    $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch { /* logging must never take the app down */ }
    }

    public static void Write(string tag, Exception ex)
    {
        if (ex is null) return;
        Write($"[{tag}] {ex.GetType().Name}: {ex.Message}");
    }

    // Call once at startup to stamp each session in the log file.
    public static void SessionHeader()
    {
        Write($"===== {AppInfo.Name} {AppInfo.Version} started — " +
              $"user={Environment.UserName}, " +
              $"elevated={SystemInfo.IsElevated}, " +
              $"os={Environment.OSVersion.Version} =====");
    }

    private static void Prune()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var f in Directory.GetFiles(AppPaths.LogsDir, "*.log")
                         .Where(f => File.GetLastWriteTime(f) < cutoff))
                File.Delete(f);
        }
        catch { /* prune is opportunistic */ }
    }
}

internal static class SystemInfo
{
    private static bool? _elevated;

    public static bool IsElevated
    {
        get
        {
            if (_elevated.HasValue) return _elevated.Value;
            try
            {
                using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var p = new System.Security.Principal.WindowsPrincipal(id);
                _elevated = p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { _elevated = false; }
            return _elevated.Value;
        }
    }
}
