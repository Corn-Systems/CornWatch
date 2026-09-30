using System.Text.Json;

namespace CornWatch;

// All user-configurable preferences.  Loaded once via settingsManager.current,
// mutated in place, and flushed via settingsManager.save() whenever they change.
internal sealed class appSettings
{
    public int windowWidth { get; set; } = 1280;
    public int windowHeight { get; set; } = 800;
    public string windowState { get; set; } = "Normal";   // "Normal" | "Maximized"
    public bool startMinimized { get; set; }

    public int pollIntervalMs { get; set; } = 1000;       // 250–30 000 ms
    public bool checkForUpdates { get; set; } = true;

    public float cpuWarnPercent { get; set; } = 70f;
    public float cpuCritPercent { get; set; } = 90f;
    public float cpuWarnTempC { get; set; } = 75f;
    public float cpuCritTempC { get; set; } = 85f;

    public float ramWarnPercent { get; set; } = 80f;
    public float ramCritPercent { get; set; } = 90f;

    // Applied per-drive.
    public float diskWarnPercent { get; set; } = 90f;
    public float diskCritPercent { get; set; } = 95f;

    public float gpuWarnTempC { get; set; } = 80f;
    public float gpuCritTempC { get; set; } = 95f;
    public float gpuVramWarnPercent { get; set; } = 85f;
    public float gpuVramCritPercent { get; set; } = 95f;
}

internal static class settingsManager
{
    // ⚠️ On-disk keys are now camelCase; case-insensitive read keeps existing PascalCase files loading.
    private static readonly JsonSerializerOptions json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<appSettings> lazy = new(load);

    public static appSettings current => lazy.Value;

    public static void save(appSettings settings)
    {
        try { appPaths.writeAtomic(appPaths.settingsFile, JsonSerializer.Serialize(settings, json)); }
        catch (Exception ex) { sessionLog.write("SETTINGS", ex); }
    }

    private static appSettings load()
    {
        try
        {
            if (File.Exists(appPaths.settingsFile)
                && JsonSerializer.Deserialize<appSettings>(File.ReadAllText(appPaths.settingsFile), json) is { } loaded)
            {
                loaded.pollIntervalMs = Math.Clamp(loaded.pollIntervalMs, 250, 30_000);
                sessionLog.write("[SETTINGS] loaded from " + appPaths.settingsFile);
                return loaded;
            }
        }
        catch (Exception ex) { sessionLog.write("SETTINGS", ex); }
        return new();
    }
}
