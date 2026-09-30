using CornWatch.Core;

namespace CornWatch.Models;

/// <summary>
/// A point-in-time snapshot of system health metrics.
/// Serialised to JSON and pushed to the WebView2 dashboard via the JS bridge.
/// </summary>
public class systemSnapshot
{
    public DateTime timestamp { get; set; } = DateTime.Now;
    public long uptimeSeconds { get; set; }

    public float cpuTotalUsage { get; set; }
    public float[] cpuCoreUsages { get; set; } = [];
    public float cpuTemperature { get; set; }
    /// <summary>False on standard user accounts (MSAcpi_ThermalZoneTemperature requires admin).</summary>
    public bool cpuTempAvailable { get; set; }
    public int cpuBaseSpeedMhz { get; set; }
    public string cpuName { get; set; } = string.Empty;

    public long ramTotalBytes { get; set; }
    public long ramUsedBytes { get; set; }
    public float ramUsagePercent => ramTotalBytes > 0 ? (float)ramUsedBytes / ramTotalBytes * 100f : 0f;

    public List<diskInfo> disks { get; set; } = [];
    public float diskReadMbps { get; set; }
    public float diskWriteMbps { get; set; }

    public float networkSentMbps { get; set; }
    public float networkReceivedMbps { get; set; }
    public string activeAdapterName { get; set; } = string.Empty;

    public List<gpuInfo> gpus { get; set; } = [];
    /// <summary>First detected GPU.</summary>
    public gpuInfo? primaryGpu => gpus.Count > 0 ? gpus[0] : null;

    public int healthScore { get; set; }
    public List<processEntry> processes { get; set; } = [];
    public List<healthAlert> alerts { get; set; } = [];
}

public class gpuInfo
{
    public string name { get; set; } = string.Empty;
    public float usagePercent { get; set; }
    public float temperatureCelsius { get; set; }
    public float vramUsedMb { get; set; }
    public float vramTotalMb { get; set; }
    public float vramUsagePercent { get; set; }
    public float fanRpm { get; set; }
    public float powerWatts { get; set; }
    public float computeUsagePercent { get; set; }
}

public class diskInfo
{
    public string driveLetter { get; set; } = string.Empty;
    public string label { get; set; } = string.Empty;
    public long totalBytes { get; set; }
    public long freeBytes { get; set; }
    public float usagePercent => totalBytes > 0 ? (float)(totalBytes - freeBytes) / totalBytes * 100f : 0f;
    public string driveFormat { get; set; } = string.Empty;
}

public class healthAlert
{
    public alertSeverity severity { get; set; }
    public string message { get; set; } = string.Empty;
    public string category { get; set; } = string.Empty;
}

public enum alertSeverity { info, warning, critical }
