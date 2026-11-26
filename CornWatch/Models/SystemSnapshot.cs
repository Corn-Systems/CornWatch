using CornWatch.Core;

namespace CornWatch.Models;

/// <summary>
/// A point-in-time snapshot of system health metrics.
/// Serialised to JSON and pushed to the WebView2 dashboard via the JS bridge.
/// </summary>
public class SystemSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public long UptimeSeconds { get; set; }

    public float CpuTotalUsage { get; set; }
    public float[] CpuCoreUsages { get; set; } = [];
    public float CpuTemperature { get; set; }
    /// <summary>False when not elevated (CPU temperature needs a kernel driver or the ACPI thermal zone).</summary>
    public bool CpuTempAvailable { get; set; }
    public int CpuBaseSpeedMhz { get; set; }
    public string CpuName { get; set; } = string.Empty;

    public long RamTotalBytes { get; set; }
    public long RamUsedBytes { get; set; }
    public float RamUsagePercent => RamTotalBytes > 0 ? (float)RamUsedBytes / RamTotalBytes * 100f : 0f;

    public List<DiskInfo> Disks { get; set; } = [];
    /// <summary>Physical drives with Windows' health verdict (and wear/temperature when elevated).</summary>
    public List<PhysicalDiskInfo> PhysicalDisks { get; set; } = [];
    public float DiskReadMbps { get; set; }
    public float DiskWriteMbps { get; set; }

    public float NetworkSentMbps { get; set; }
    public float NetworkReceivedMbps { get; set; }
    public string ActiveAdapterName { get; set; } = string.Empty;

    /// <summary>Every hardware GPU, largest VRAM first.</summary>
    public List<GpuInfo> Gpus { get; set; } = [];

    /// <summary>Null on PCs without a battery.</summary>
    public BatteryInfo? Battery { get; set; }

    public int HealthScore { get; set; }
    public List<ProcessEntry> Processes { get; set; } = [];
    public List<HealthAlert> Alerts { get; set; } = [];
}

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public float UsagePercent { get; set; }
    // Null when the vendor exposes no such sensor (the dashboard shows N/A, not zero).
    public float? TemperatureCelsius { get; set; }
    public float VramUsedMb { get; set; }
    public float VramTotalMb { get; set; }
    public float VramUsagePercent { get; set; }
    public float? FanRpm { get; set; }
    public float? PowerWatts { get; set; }
    public float ComputeUsagePercent { get; set; }
}

public class DiskInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public float UsagePercent => TotalBytes > 0 ? (float)(TotalBytes - FreeBytes) / TotalBytes * 100f : 0f;
    public string DriveFormat { get; set; } = string.Empty;
    /// <summary>USB sticks, SD cards: shown on the dashboard but not scored — a full flash drive isn't a system problem.</summary>
    public bool IsRemovable { get; set; }
}

public class PhysicalDiskInfo
{
    public string Name { get; set; } = string.Empty;
    /// <summary>"NVMe", "SSD", "HDD" or "Drive" when Windows doesn't say.</summary>
    public string Kind { get; set; } = "Drive";
    public long SizeBytes { get; set; }
    public DriveHealth Health { get; set; } = DriveHealth.Unknown;
    public float? TemperatureCelsius { get; set; }
    /// <summary>Share of the drive's rated write endurance used up (0 = new). Null when not readable.</summary>
    public float? WearPercent { get; set; }
    /// <summary>USB / SD: shown, but not scored.</summary>
    public bool IsRemovable { get; set; }
}

public enum DriveHealth { Healthy, Warning, Unhealthy, Unknown }

public class BatteryInfo
{
    public float ChargePercent { get; set; }
    public bool OnAcPower { get; set; }
    public bool IsCharging { get; set; }
    /// <summary>Charge or discharge rate, always positive. Null when the battery doesn't report it.</summary>
    public float? RateWatts { get; set; }
    /// <summary>Capacity lost versus design (0 = like new).</summary>
    public float? WearPercent { get; set; }
    public float? DesignedCapacityWh { get; set; }
    public float? FullChargeCapacityWh { get; set; }
    /// <summary>Estimated time until empty while discharging; null otherwise or when unknown.</summary>
    public int? RemainingSeconds { get; set; }
}

public class HealthAlert
{
    /// <summary>Stable key for the check that raised this alert (e.g. "cpu.usage", "disk.C:\\").</summary>
    public string Id { get; set; } = string.Empty;
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public enum AlertSeverity { Info, Warning, Critical }
