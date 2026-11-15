using System.Text;

namespace CornWatch;

// Persistent per-day log at %AppData%\CornSystems\CornWatch\logs\yyyy-MM-dd.log.
// Every swallowed exception in the codebase reports here instead of vanishing
// into an empty catch {}.
internal static class SessionLog
{
    private const int RetentionDays = 14;
    private static readonly object _gate = new();
    private static bool _pruned;
    private static string? _lastMessage;
    private static int _repeats;

    public static void Write(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        try
        {
            var now = DateTime.Now;
            lock (_gate)
            {
                // A fault that recurs every poll tick is collapsed into one line plus a repeat count.
                if (message == _lastMessage) { _repeats++; return; }

                AppPaths.EnsureDir(AppPaths.LogsDir);
                if (!_pruned) { Prune(); _pruned = true; }
                var text = $"[{now:HH:mm:ss}] {message}{Environment.NewLine}";
                if (_repeats > 0) text = $"[{now:HH:mm:ss}] (previous message repeated {_repeats} more times){Environment.NewLine}{text}";
                _lastMessage = message;
                _repeats = 0;
                File.AppendAllText(Path.Combine(AppPaths.LogsDir, $"{now:yyyy-MM-dd}.log"), text, Encoding.UTF8);
            }
        }
        catch { /* logging must never take the app down */ }
    }

    public static void Write(string tag, Exception ex) =>
        Write($"[{tag}] {ex.GetType().Name}: {ex.Message}");

    // Call once at startup to stamp each session in the log file.
    public static void SessionHeader() =>
        Write($"===== {AppInfo.Name} {AppInfo.Version} started — user={Environment.UserName}, " +
              $"elevated={AppInfo.IsElevated}, os={Environment.OSVersion.Version} =====");

    private static void Prune()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var f in Directory.GetFiles(AppPaths.LogsDir, "*.log").Where(p => File.GetLastWriteTime(p) < cutoff))
                File.Delete(f);
        }
        catch { /* prune is opportunistic */ }
    }
}
