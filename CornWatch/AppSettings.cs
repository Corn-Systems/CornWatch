using System.Text.Json;

namespace CornWatch;

// All user-configurable preferences.  Loaded once via SettingsManager.Current,
// mutated in place, and flushed via SettingsManager.Save() whenever they change.
internal sealed class AppSettings
{
    public const int MinPollIntervalMs = 250, MaxPollIntervalMs = 30_000;

    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public string WindowState { get; set; } = "Normal";   // "Normal" | "Maximized"
    public bool StartMinimized { get; set; }

    public int PollIntervalMs { get; set; } = 1000;
    public bool CheckForUpdates { get; set; } = true;
    public bool NotifyOnCritical { get; set; } = true;    // tray balloon when a critical alert appears

    public float CpuWarnPercent { get; set; } = 70f;
    public float CpuCritPercent { get; set; } = 90f;
    public float CpuWarnTempC { get; set; } = 75f;
    public float CpuCritTempC { get; set; } = 85f;

    public float RamWarnPercent { get; set; } = 80f;
    public float RamCritPercent { get; set; } = 90f;

    // Applied per-drive.
    public float DiskWarnPercent { get; set; } = 90f;
    public float DiskCritPercent { get; set; } = 95f;

    public float GpuWarnTempC { get; set; } = 80f;
    public float GpuCritTempC { get; set; } = 95f;
    public float GpuVramWarnPercent { get; set; } = 85f;
    public float GpuVramCritPercent { get; set; } = 95f;

    public static int ClampPollInterval(int ms) => Math.Clamp(ms, MinPollIntervalMs, MaxPollIntervalMs);

    // Forces every value into a sane range so a hand-edited file or bad UI input can't break alerting.
    public void Normalize()
    {
        PollIntervalMs = ClampPollInterval(PollIntervalMs);
        (CpuWarnPercent, CpuCritPercent) = Pair(CpuWarnPercent, CpuCritPercent, 100f);
        (RamWarnPercent, RamCritPercent) = Pair(RamWarnPercent, RamCritPercent, 100f);
        (DiskWarnPercent, DiskCritPercent) = Pair(DiskWarnPercent, DiskCritPercent, 100f);
        (GpuVramWarnPercent, GpuVramCritPercent) = Pair(GpuVramWarnPercent, GpuVramCritPercent, 100f);
        (CpuWarnTempC, CpuCritTempC) = Pair(CpuWarnTempC, CpuCritTempC, 150f);
        (GpuWarnTempC, GpuCritTempC) = Pair(GpuWarnTempC, GpuCritTempC, 150f);
    }

    private static (float warn, float crit) Pair(float warn, float crit, float max)
    {
        crit = float.IsFinite(crit) ? Math.Clamp(crit, 1f, max) : max;
        warn = float.IsFinite(warn) ? Math.Clamp(warn, 1f, crit) : crit;
        return (warn, crit);
    }
}

internal static class SettingsManager
{
    // On-disk keys are camelCase; case-insensitive read keeps older PascalCase files loading.
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Lazy<AppSettings> _lazy = new(Load);

    public static AppSettings Current => _lazy.Value;

    public static string ToJson() => JsonSerializer.Serialize(Current, _json);

    public static void Save()
    {
        try { AppPaths.WriteAtomic(AppPaths.SettingsFile, ToJson()); }
        catch (Exception ex) { SessionLog.Write("SETTINGS", ex); }
    }

    // Applies settings edited in the dashboard. Window geometry is owned by the form, so it is kept as-is.
    public static void Update(string incomingJson)
    {
        var incoming = JsonSerializer.Deserialize<AppSettings>(incomingJson, _json)
            ?? throw new JsonException("Settings payload was empty.");
        incoming.Normalize();
        incoming.WindowWidth = Current.WindowWidth;
        incoming.WindowHeight = Current.WindowHeight;
        incoming.WindowState = Current.WindowState;

        foreach (var prop in typeof(AppSettings).GetProperties())
            prop.SetValue(Current, prop.GetValue(incoming));
        Save();
        SessionLog.Write("[SETTINGS] updated from dashboard");
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile)
                && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), _json) is { } loaded)
            {
                loaded.Normalize();
                SessionLog.Write("[SETTINGS] loaded from " + AppPaths.SettingsFile);
                return loaded;
            }
        }
        catch (Exception ex) { SessionLog.Write("SETTINGS", ex); }
        return new();
    }
}
