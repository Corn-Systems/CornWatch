namespace CornWatch;

// Every on-disk path the app uses, in one place.
internal static class appPaths
{
    public static string dataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CornSystems", appInfo.name);

    public static string settingsFile { get; } = Path.Combine(dataDir, "settings.json");
    public static string historyFile { get; } = Path.Combine(dataDir, "history.json");
    public static string logsDir { get; } = Path.Combine(dataDir, "logs");

    private static string snapshotsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appInfo.name, "Snapshots");

    public static void ensureDir(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }
    }

    // Documents\CornWatch\Snapshots\snapshot_<timestamp>.<ext>; creates the folder.
    public static string snapshotFile(DateTime timestamp, string ext)
    {
        ensureDir(snapshotsDir);
        return Path.Combine(snapshotsDir, $"snapshot_{timestamp:yyyy-MM-dd_HH-mm-ss}.{ext}");
    }

    // Temp-file then rename so a crash mid-write can't corrupt the target.
    public static void writeAtomic(string file, string text)
    {
        ensureDir(dataDir);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, file, overwrite: true);
    }
}
