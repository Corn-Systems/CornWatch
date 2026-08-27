using CornWatch.Core;

namespace CornWatch.Models;

/// <summary>
/// A point-in-time snapshot of system health metrics.
/// Serialized to JSON and sent to the WebView2 dashboard via JS bridge.
/// </summary>
public class SystemSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // ── System ───────────────────────────────────────────────────────────────
    public long UptimeSeconds { get; set; }

    // ── CPU ──────────────────────────────────────────────────────────────────
    public float CpuTotalUsage { get; set; }
    public float[] CpuCoreUsages { get; set; } = [];
    public float CpuTemperature { get; set; }
    public int CpuBaseSpeedMhz { get; set; }
    public string CpuName { get; set; } = string.Empty;

    // ── RAM ──────────────────────────────────────────────────────────────────
    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public float RamUsagePercent => RamTotalBytes > 0
        ? (float)RamUsedBytes / RamTotalBytes * 100f
        : 0f;

    // ── Disk ─────────────────────────────────────────────────────────────────
    public List<DiskInfo> Disks { get; set; } = [];
    public float DiskReadMbps { get; set; }
    public float DiskWriteMbps { get; set; }

    // ── Network ──────────────────────────────────────────────────────────────
    public float NetworkSentMbps { get; set; }
    public float NetworkReceivedMbps { get; set; }
    public string ActiveAdapterName { get; set; } = string.Empty;

    // ── GPU ──────────────────────────────────────────────────────────────────
    public List<GpuInfo> Gpus { get; set; } = [];

    // Convenience accessors for the primary GPU (first detected)
    public GpuInfo? PrimaryGpu => Gpus.Count > 0 ? Gpus[0] : null;

    // ── Health Score ─────────────────────────────────────────────────────────
    public int HealthScore { get; set; }

    // ── Processes ────────────────────────────────────────────────────────────
    public List<ProcessEntry> Processes { get; set; } = [];

    // ── Alerts ───────────────────────────────────────────────────────────────
    public List<HealthAlert> Alerts { get; set; } = [];
}

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public float UsagePercent { get; set; }       // 0–100%
    public float TemperatureCelsius { get; set; } // °C
    public float VramUsedMb { get; set; }         // MB
    public float VramTotalMb { get; set; }        // MB
    public float VramUsagePercent { get; set; }   // 0–100%
    public float FanRpm { get; set; }             // RPM
    public float CoreClockMhz { get; set; }       // MHz
    public float PowerWatts { get; set; }         // Watts

    // AMD-specific: D3D engine workloads (D3D 3D, Compute, Copy, etc.)
    public float ComputeUsagePercent { get; set; }
    public Dictionary<string, float> D3DEngines { get; set; } = [];
}

public class DiskInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public float UsagePercent => TotalBytes > 0
        ? (float)(TotalBytes - FreeBytes) / TotalBytes * 100f
        : 0f;
    public string DriveFormat { get; set; } = string.Empty;
}

public class HealthAlert
{
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public enum AlertSeverity { Info, Warning, Critical }