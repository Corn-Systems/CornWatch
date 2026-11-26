namespace CornWatch;

// Every on-disk path the app uses, in one place.
internal static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CornSystems", AppInfo.Name);

    public static string SettingsFile { get; } = Path.Combine(DataDir, "settings.json");
    public static string HistoryFile { get; } = Path.Combine(DataDir, "history.json");
    public static string LogsDir { get; } = Path.Combine(DataDir, "logs");

    // WebView2 profile. Must be user-writable: the default (next to the exe) fails under Program Files.
    public static string WebViewDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CornSystems", AppInfo.Name, "WebView2");

    // The dashboard page is embedded in the exe (so the portable build is one file) and written
    // here for WebView2 to load.
    public static string DashboardFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CornSystems", AppInfo.Name, "UI", "Dashboard.html");

    private static string SnapshotsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppInfo.Name, "Snapshots");

    public static void EnsureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
    }

    // Documents\CornWatch\Snapshots\snapshot_<timestamp>.<ext>; creates the folder.
    public static string SnapshotFile(DateTime timestamp, string ext)
    {
        EnsureDir(SnapshotsDir);
        return Path.Combine(SnapshotsDir, $"snapshot_{timestamp:yyyy-MM-dd_HH-mm-ss}.{ext}");
    }

    // Temp-file then rename so a crash mid-write can't corrupt the target.
    public static void WriteAtomic(string file, string text)
    {
        EnsureDir(DataDir);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, file, overwrite: true);
    }
}
