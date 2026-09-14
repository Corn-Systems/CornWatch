namespace CornWatch;

// Every on-disk path the app uses, in one place.
// Nothing else in the codebase calls Path.Combine(Environment.GetFolderPath(...), ...)
// directly — go through here instead.
internal static class AppPaths
{
    public const string Vendor    = "CornSystems";
    public const string AppFolder = "CornWatch";

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Vendor, AppFolder);

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string CrashLog     => Path.Combine(DataDir, "crash.log");
    public static string LogsDir      => Path.Combine(DataDir, "logs");

    // Snapshot exports go to Documents\CornWatch\Snapshots — same location
    // used by SnapshotExporter and ExportPngFromUi.
    public static string SnapshotsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                     AppFolder, "Snapshots");

    // History DB lives alongside settings, not in Snapshots.
    public static string HistoryFile => Path.Combine(DataDir, "history.json");

    public static void EnsureDataDir()
    {
        try { Directory.CreateDirectory(DataDir); } catch { /* best-effort */ }
    }

    public static void EnsureLogsDir()
    {
        try { Directory.CreateDirectory(LogsDir); } catch { }
    }

    public static void EnsureSnapshotsDir()
    {
        try { Directory.CreateDirectory(SnapshotsDir); } catch { }
    }
}
