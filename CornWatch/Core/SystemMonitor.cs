using System.Diagnostics;
using System.Management;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Polls system metrics on a background timer.
/// Raises SnapshotReady with fresh data every tick.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<SystemSnapshot>? SnapshotReady;

    public int PollIntervalMs { get; set; } = 1000;

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly System.Threading.Timer _timer;
    private readonly PerformanceCounter _cpuTotal;
    private readonly List<PerformanceCounter> _cpuCores = [];
    private readonly PerformanceCounter _diskRead;
    private readonly PerformanceCounter _diskWrite;
    private readonly PerformanceCounter _netSent;
    private readonly PerformanceCounter _netRecv;
    private readonly GpuMonitor _gpuMonitor;
    private readonly ProcessWatchdog _procWatchdog;
    private readonly string _adapterName;
    public string GpuSensorDump => _gpuMonitor.DebugSensorDump;
    private bool _disposed;

    // Cached once — CPU name and base clock don't change at runtime,
    // so no need to hit WMI for them every second.
    private static string _cachedCpuName = string.Empty;
    private static int    _cachedCpuBaseMhz;

    public SystemMonitor()
    {
        _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");

        var category = new PerformanceCounterCategory("Processor");
        foreach (var instance in category.GetInstanceNames()
            .Where(n => n != "_Total")
            .OrderBy(n => n))
        {
            _cpuCores.Add(new PerformanceCounter("Processor", "% Processor Time", instance));
        }

        _diskRead  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total");
        _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

        var netCategory = new PerformanceCounterCategory("Network Interface");
        var instances   = netCategory.GetInstanceNames();
        string[] virtualHints =
            ["loopback", "isatap", "teredo", "bluetooth", "virtual",
             "vmware", "vbox", "vethernet", "tunnel", "tap-"];
        var adapter = instances.FirstOrDefault(n =>
                !virtualHints.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)))
            ?? instances.FirstOrDefault(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            ?? instances.FirstOrDefault() ?? "Ethernet";
        _adapterName = adapter;

        _netSent = new PerformanceCounter("Network Interface", "Bytes Sent/sec",     adapter);
        _netRecv = new PerformanceCounter("Network Interface", "Bytes Received/sec", adapter);

        // Prime counters
        _ = _cpuTotal.NextValue();
        foreach (var c in _cpuCores) _ = c.NextValue();

        _gpuMonitor   = new GpuMonitor();
        _procWatchdog = new ProcessWatchdog(topN: 8);

        _timer = new System.Threading.Timer(_ => Poll(), null,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(PollIntervalMs));
    }

    /// <summary>
    /// Changes the polling rate at runtime (PollIntervalMs alone never
    /// reprogrammed the timer). Used to ease off to 5s while in the tray.
    /// </summary>
    public void SetPollInterval(int ms)
    {
        if (_disposed || ms < 250) return;
        PollIntervalMs = ms;
        try { _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)); }
        catch { }
    }

    private void Poll()
    {
        if (_disposed) return;
        try { SnapshotReady?.Invoke(BuildSnapshot()); }
        catch { }
    }

    private SystemSnapshot BuildSnapshot()
    {
        var snap = new SystemSnapshot
        {
            CpuTotalUsage       = _cpuTotal.NextValue(),
            CpuCoreUsages       = _cpuCores.Select(c => c.NextValue()).ToArray(),
            DiskReadMbps        = _diskRead.NextValue()  / 1_048_576f,
            DiskWriteMbps       = _diskWrite.NextValue() / 1_048_576f,
            NetworkSentMbps     = _netSent.NextValue() / 1_048_576f,
            NetworkReceivedMbps = _netRecv.NextValue() / 1_048_576f,
            ActiveAdapterName   = _adapterName,
            UptimeSeconds       = Environment.TickCount64 / 1000,
        };

        EnrichFromWmi(snap);
        EnrichRam(snap);
        EnrichDisks(snap);
        EnrichGpu(snap);
        EnrichProcesses(snap);

        snap.HealthScore = CalculateHealthScore(snap);
        snap.Alerts      = GenerateAlerts(snap);

        return snap;
    }

    // ── Processes ────────────────────────────────────────────────────────────
    private void EnrichProcesses(SystemSnapshot snap)
    {
        try { snap.Processes = _procWatchdog.Read(); }
        catch { }
    }

    // ── GPU ───────────────────────────────────────────────────────────────────
    private void EnrichGpu(SystemSnapshot snap)
    {
        try { snap.Gpus = _gpuMonitor.Read(); }
        catch { }
    }

    // ── WMI ───────────────────────────────────────────────────────────────────
    private static void EnrichFromWmi(SystemSnapshot snap)
    {
        if (string.IsNullOrEmpty(_cachedCpuName))
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, CurrentClockSpeed FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    _cachedCpuName    = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                    _cachedCpuBaseMhz = Convert.ToInt32(obj["CurrentClockSpeed"]);
                    break;
                }
            }
            catch { }
        }
        snap.CpuName         = _cachedCpuName;
        snap.CpuBaseSpeedMhz = _cachedCpuBaseMhz;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                snap.CpuTemperature = (float)(Convert.ToDouble(obj["CurrentTemperature"]) / 10.0 - 273.15);
                break;
            }
        }
        catch { }
    }

    private static void EnrichRam(SystemSnapshot snap)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var total = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024L;
                var free  = Convert.ToInt64(obj["FreePhysicalMemory"])     * 1024L;
                snap.RamTotalBytes = total;
                snap.RamUsedBytes  = total - free;
                break;
            }
        }
        catch { }
    }

    private static void EnrichDisks(SystemSnapshot snap)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            snap.Disks.Add(new DiskInfo
            {
                DriveLetter = drive.Name,
                Label       = drive.VolumeLabel,
                TotalBytes  = drive.TotalSize,
                FreeBytes   = drive.TotalFreeSpace,
                DriveFormat = drive.DriveFormat,
            });
        }
    }

    // ── Health score ─────────────────────────────────────────────────────────
    private static int CalculateHealthScore(SystemSnapshot s)
    {
        float score = 100f;

        if (s.CpuTotalUsage > 90) score -= 20;
        else if (s.CpuTotalUsage > 70) score -= 10;

        if (s.CpuTemperature > 90) score -= 25;
        else if (s.CpuTemperature > 75) score -= 10;

        if (s.RamUsagePercent > 90) score -= 20;
        else if (s.RamUsagePercent > 80) score -= 10;

        foreach (var disk in s.Disks)
        {
            if (disk.UsagePercent > 95) score -= 15;
            else if (disk.UsagePercent > 90) score -= 8;
        }

        // GPU penalties
        if (s.PrimaryGpu is { } gpu)
        {
            if (gpu.TemperatureCelsius > 95) score -= 25;
            else if (gpu.TemperatureCelsius > 80) score -= 10;

            if (gpu.VramUsagePercent > 95) score -= 15;
            else if (gpu.VramUsagePercent > 85) score -= 7;
        }

        return Math.Max(0, (int)score);
    }

    private static List<HealthAlert> GenerateAlerts(SystemSnapshot s)
    {
        var alerts = new List<HealthAlert>();

        if (s.CpuTotalUsage > 90)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "CPU",  Message = $"CPU at {s.CpuTotalUsage:0}% — unusually high" });
        else if (s.CpuTotalUsage > 70)
            alerts.Add(new() { Severity = AlertSeverity.Warning,  Category = "CPU",  Message = $"CPU at {s.CpuTotalUsage:0}%" });

        if (s.CpuTemperature > 85)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "CPU",  Message = $"CPU temp {s.CpuTemperature:0}°C — consider cooling" });

        if (s.RamUsagePercent > 90)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "RAM",  Message = $"RAM at {s.RamUsagePercent:0}% — close some apps" });

        foreach (var disk in s.Disks.Where(d => d.UsagePercent > 90))
            alerts.Add(new() { Severity = AlertSeverity.Warning,  Category = "Disk", Message = $"{disk.DriveLetter} is {disk.UsagePercent:0}% full" });

        // GPU alerts
        if (s.PrimaryGpu is { } gpu)
        {
            if (gpu.TemperatureCelsius > 95)
                alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "GPU", Message = $"GPU temp {gpu.TemperatureCelsius:0}°C — critical!" });
            else if (gpu.TemperatureCelsius > 80)
                alerts.Add(new() { Severity = AlertSeverity.Warning,  Category = "GPU", Message = $"GPU temp {gpu.TemperatureCelsius:0}°C — running warm" });

            if (gpu.VramUsagePercent > 95)
                alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "GPU", Message = $"VRAM nearly full ({gpu.VramUsedMb:0}/{gpu.VramTotalMb:0} MB)" });
        }

        return alerts;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _cpuTotal.Dispose();
        foreach (var c in _cpuCores) c.Dispose();
        _diskRead.Dispose(); _diskWrite.Dispose();
        _netSent.Dispose();  _netRecv.Dispose();
        _gpuMonitor.Dispose();
        _procWatchdog.Dispose();
    }
}
