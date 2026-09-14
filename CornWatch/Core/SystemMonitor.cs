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
    private readonly System.Threading.Timer   _timer;
    private readonly PerformanceCounter?      _cpuTotal;
    private readonly List<PerformanceCounter> _cpuCores  = [];
    private readonly PerformanceCounter?      _diskRead;
    private readonly PerformanceCounter?      _diskWrite;
    private readonly PerformanceCounter?      _netSent;
    private readonly PerformanceCounter?      _netRecv;
    private readonly GpuMonitor               _gpuMonitor;
    private readonly ProcessWatchdog          _procWatchdog;
    private readonly string                   _adapterName;
    private          bool                     _disposed;

    public string GpuSensorDump => _gpuMonitor.DebugSensorDump;

    // Cached once — CPU name and base clock don't change at runtime.
    private static string _cachedCpuName    = string.Empty;
    private static int    _cachedCpuBaseMhz;

    public SystemMonitor()
    {
        // ── CPU total ─────────────────────────────────────────────────────────
        try
        {
            _cpuTotal = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ = _cpuTotal.NextValue(); // prime

            var category = new PerformanceCounterCategory("Processor");
            foreach (var inst in category.GetInstanceNames()
                         .Where(n => n != "_Total")
                         .OrderBy(n => n))
            {
                try
                {
                    var c = new PerformanceCounter("Processor", "% Processor Time", inst);
                    _ = c.NextValue(); // prime
                    _cpuCores.Add(c);
                }
                catch (Exception ex) { SessionLog.Write($"[MONITOR] CPU core counter '{inst}'", ex); }
            }
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_CPU_INIT", ex); }

        // ── Disk ──────────────────────────────────────────────────────────────
        try
        {
            _diskRead  = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec",  "_Total");
            _diskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _ = _diskRead.NextValue(); _ = _diskWrite.NextValue();
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_DISK_INIT", ex); }

        // ── Network adapter selection ─────────────────────────────────────────
        _adapterName = PickNetworkAdapter();
        try
        {
            if (!string.IsNullOrEmpty(_adapterName))
            {
                _netSent = new PerformanceCounter("Network Interface", "Bytes Sent/sec",     _adapterName);
                _netRecv = new PerformanceCounter("Network Interface", "Bytes Received/sec", _adapterName);
                _ = _netSent.NextValue(); _ = _netRecv.NextValue();
            }
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_NET_INIT", ex); }

        // ── GPU + processes ───────────────────────────────────────────────────
        _gpuMonitor   = new GpuMonitor();
        _procWatchdog = new ProcessWatchdog(topN: 8);

        _timer = new System.Threading.Timer(_ => Poll(), null,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(PollIntervalMs));
    }

    // ── Adapter picker ────────────────────────────────────────────────────────
    private static string PickNetworkAdapter()
    {
        try
        {
            var netCategory = new PerformanceCounterCategory("Network Interface");
            var instances   = netCategory.GetInstanceNames();
            if (instances.Length == 0) return string.Empty;

            string[] virtualHints =
                ["loopback", "isatap", "teredo", "bluetooth", "virtual",
                 "vmware", "vbox", "vethernet", "tunnel", "tap-"];

            // First: a non-virtual adapter with actual traffic counters.
            var chosen = instances.FirstOrDefault(n =>
                !virtualHints.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)));

            // Second: anything that isn't Loopback.
            chosen ??= instances.FirstOrDefault(
                n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase));

            // Last resort: just take whatever exists.
            chosen ??= instances.FirstOrDefault();

            if (chosen is null)
            {
                SessionLog.Write("[MONITOR] No network adapters found in perf counter category.");
                return string.Empty;
            }

            SessionLog.Write($"[MONITOR] Selected network adapter: {chosen}");
            return chosen;
        }
        catch (Exception ex)
        {
            SessionLog.Write("MONITOR_NET_ADAPTER", ex);
            return string.Empty;
        }
    }

    // ── Poll rate ─────────────────────────────────────────────────────────────
    public void SetPollInterval(int ms)
    {
        if (_disposed || ms < 250) return;
        PollIntervalMs = ms;
        try { _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)); }
        catch (Exception ex) { SessionLog.Write("MONITOR_INTERVAL", ex); }
    }

    // ── Core poll ─────────────────────────────────────────────────────────────
    private void Poll()
    {
        if (_disposed) return;
        try
        {
            var snap = BuildSnapshot();
            HealthHistory.Instance.Append(snap.HealthScore, snap.Alerts.Count);
            SnapshotReady?.Invoke(snap);
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_POLL", ex); }
    }

    private SystemSnapshot BuildSnapshot()
    {
        var snap = new SystemSnapshot
        {
            CpuTotalUsage   = SafeNextValue(_cpuTotal),
            CpuCoreUsages   = _cpuCores.Select(c => SafeNextValue(c)).ToArray(),
            DiskReadMbps    = SafeNextValue(_diskRead)  / 1_048_576f,
            DiskWriteMbps   = SafeNextValue(_diskWrite) / 1_048_576f,
            NetworkSentMbps     = SafeNextValue(_netSent) / 1_048_576f,
            NetworkReceivedMbps = SafeNextValue(_netRecv) / 1_048_576f,
            ActiveAdapterName   = _adapterName,
            UptimeSeconds       = Environment.TickCount64 / 1000,
        };

        EnrichFromWmi(snap);
        EnrichRam(snap);
        EnrichDisks(snap);
        EnrichGpu(snap);
        EnrichProcesses(snap);

        var s = SettingsManager.Current;
        snap.HealthScore = CalculateHealthScore(snap, s);
        snap.Alerts      = GenerateAlerts(snap, s);

        return snap;
    }

    private static float SafeNextValue(PerformanceCounter? counter)
    {
        if (counter is null) return 0f;
        try { return counter.NextValue(); }
        catch (Exception ex) { SessionLog.Write("MONITOR_COUNTER", ex); return 0f; }
    }

    // ── Enrichment ────────────────────────────────────────────────────────────

    private void EnrichProcesses(SystemSnapshot snap)
    {
        try { snap.Processes = _procWatchdog.Read(); }
        catch (Exception ex) { SessionLog.Write("MONITOR_PROCS", ex); }
    }

    private void EnrichGpu(SystemSnapshot snap)
    {
        try { snap.Gpus = _gpuMonitor.Read(); }
        catch (Exception ex) { SessionLog.Write("MONITOR_GPU", ex); }
    }

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
            catch (Exception ex) { SessionLog.Write("MONITOR_WMI_CPU", ex); }
        }
        snap.CpuName         = _cachedCpuName;
        snap.CpuBaseSpeedMhz = _cachedCpuBaseMhz;

        // CPU temperature — requires admin; silently returns 0 otherwise.
        // Dashboard shows "N/A (run as admin)" when value is 0.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            foreach (ManagementObject obj in searcher.Get())
            {
                snap.CpuTemperature    = (float)(Convert.ToDouble(obj["CurrentTemperature"]) / 10.0 - 273.15);
                snap.CpuTempAvailable  = true;
                break;
            }
        }
        catch
        {
            // No log here — this fires every second for standard users; would spam the log.
            snap.CpuTempAvailable = false;
        }
    }

    private static void EnrichRam(SystemSnapshot snap)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                var total          = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024L;
                var free           = Convert.ToInt64(obj["FreePhysicalMemory"])     * 1024L;
                snap.RamTotalBytes = total;
                snap.RamUsedBytes  = total - free;
                break;
            }
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_WMI_RAM", ex); }
    }

    private static void EnrichDisks(SystemSnapshot snap)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            try
            {
                snap.Disks.Add(new DiskInfo
                {
                    DriveLetter = drive.Name,
                    Label       = drive.VolumeLabel,
                    TotalBytes  = drive.TotalSize,
                    FreeBytes   = drive.TotalFreeSpace,
                    DriveFormat = drive.DriveFormat,
                });
            }
            catch (Exception ex) { SessionLog.Write($"[MONITOR] drive {drive.Name}", ex); }
        }
    }

    // ── Health score ─────────────────────────────────────────────────────────
    private static int CalculateHealthScore(SystemSnapshot s, AppSettings cfg)
    {
        float score = 100f;

        if (s.CpuTotalUsage > cfg.CpuCritPercent)      score -= 20;
        else if (s.CpuTotalUsage > cfg.CpuWarnPercent) score -= 10;

        if (s.CpuTempAvailable)
        {
            if (s.CpuTemperature > cfg.CpuCritTempC)      score -= 25;
            else if (s.CpuTemperature > cfg.CpuWarnTempC) score -= 10;
        }

        if (s.RamUsagePercent > cfg.RamCritPercent)      score -= 20;
        else if (s.RamUsagePercent > cfg.RamWarnPercent) score -= 10;

        foreach (var disk in s.Disks)
        {
            if (disk.UsagePercent > cfg.DiskCritPercent)      score -= 15;
            else if (disk.UsagePercent > cfg.DiskWarnPercent) score -= 8;
        }

        if (s.PrimaryGpu is { } gpu)
        {
            if (gpu.TemperatureCelsius > cfg.GpuCritTempC)       score -= 25;
            else if (gpu.TemperatureCelsius > cfg.GpuWarnTempC)  score -= 10;

            if (gpu.VramUsagePercent > cfg.GpuVramCritPercent)   score -= 15;
            else if (gpu.VramUsagePercent > cfg.GpuVramWarnPercent) score -= 7;
        }

        return Math.Max(0, (int)score);
    }

    private static List<HealthAlert> GenerateAlerts(SystemSnapshot s, AppSettings cfg)
    {
        var alerts = new List<HealthAlert>();

        if (s.CpuTotalUsage > cfg.CpuCritPercent)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "CPU",
                Message = $"CPU at {s.CpuTotalUsage:0}% — unusually high" });
        else if (s.CpuTotalUsage > cfg.CpuWarnPercent)
            alerts.Add(new() { Severity = AlertSeverity.Warning,  Category = "CPU",
                Message = $"CPU at {s.CpuTotalUsage:0}%" });

        if (s.CpuTempAvailable && s.CpuTemperature > cfg.CpuCritTempC)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "CPU",
                Message = $"CPU temp {s.CpuTemperature:0}°C — consider cooling" });
        else if (s.CpuTempAvailable && s.CpuTemperature > cfg.CpuWarnTempC)
            alerts.Add(new() { Severity = AlertSeverity.Warning, Category = "CPU",
                Message = $"CPU temp {s.CpuTemperature:0}°C" });

        if (s.RamUsagePercent > cfg.RamCritPercent)
            alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "RAM",
                Message = $"RAM at {s.RamUsagePercent:0}% — close some apps" });

        foreach (var disk in s.Disks.Where(d => d.UsagePercent > cfg.DiskWarnPercent))
            alerts.Add(new() { Severity = AlertSeverity.Warning, Category = "Disk",
                Message = $"{disk.DriveLetter} is {disk.UsagePercent:0}% full" });

        if (s.PrimaryGpu is { } gpu)
        {
            if (gpu.TemperatureCelsius > cfg.GpuCritTempC)
                alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "GPU",
                    Message = $"GPU temp {gpu.TemperatureCelsius:0}°C — critical!" });
            else if (gpu.TemperatureCelsius > cfg.GpuWarnTempC)
                alerts.Add(new() { Severity = AlertSeverity.Warning,  Category = "GPU",
                    Message = $"GPU temp {gpu.TemperatureCelsius:0}°C — running warm" });

            if (gpu.VramUsagePercent > cfg.GpuVramCritPercent)
                alerts.Add(new() { Severity = AlertSeverity.Critical, Category = "GPU",
                    Message = $"VRAM nearly full ({gpu.VramUsedMb:0}/{gpu.VramTotalMb:0} MB)" });
        }

        return alerts;
    }

    // ── Dispose ───────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        _cpuTotal?.Dispose();
        foreach (var c in _cpuCores) c.Dispose();
        _diskRead?.Dispose();  _diskWrite?.Dispose();
        _netSent?.Dispose();   _netRecv?.Dispose();
        _gpuMonitor.Dispose();
        _procWatchdog.Dispose();
    }
}
