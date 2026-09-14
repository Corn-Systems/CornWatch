using System.Text.Json;

namespace CornWatch;

// All user-configurable preferences.  Loaded once at startup via SettingsManager.Load(),
// mutated in place, and flushed via SettingsManager.Save() whenever they change.
internal class AppSettings
{
    // ── Window geometry ───────────────────────────────────────────────────────
    public int    WindowWidth    { get; set; } = 1280;
    public int    WindowHeight   { get; set; } = 800;
    public string WindowState    { get; set; } = "Normal";   // "Normal" | "Maximized"
    public bool   StartMinimized { get; set; } = false;

    // ── Polling ───────────────────────────────────────────────────────────────
    public int PollIntervalMs { get; set; } = 1000;  // 250–30 000 ms

    // ── Update check ──────────────────────────────────────────────────────────
    public bool CheckForUpdates { get; set; } = true;

    // ── Alert thresholds ──────────────────────────────────────────────────────
    // CPU
    public float CpuWarnPercent     { get; set; } = 70f;
    public float CpuCritPercent     { get; set; } = 90f;
    public float CpuWarnTempC       { get; set; } = 75f;
    public float CpuCritTempC       { get; set; } = 85f;

    // RAM
    public float RamWarnPercent     { get; set; } = 80f;
    public float RamCritPercent     { get; set; } = 90f;

    // Disk (applied per-drive)
    public float DiskWarnPercent    { get; set; } = 90f;
    public float DiskCritPercent    { get; set; } = 95f;

    // GPU
    public float GpuWarnTempC       { get; set; } = 80f;
    public float GpuCritTempC       { get; set; } = 95f;
    public float GpuVramWarnPercent { get; set; } = 85f;
    public float GpuVramCritPercent { get; set; } = 95f;
}

internal static class SettingsManager
{
    private static readonly JsonSerializerOptions _json =
        new() { WriteIndented = true };

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(AppPaths.SettingsFile), _json);
                if (loaded is not null)
                {
                    SessionLog.Write("[SETTINGS] loaded from " + AppPaths.SettingsFile);
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write("SETTINGS", ex);
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            AppPaths.EnsureDataDir();
            // Atomic write: temp-file then rename so a crash mid-write
            // can't corrupt settings.json.
            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, _json));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            SessionLog.Write("SETTINGS", ex);
        }
    }
}