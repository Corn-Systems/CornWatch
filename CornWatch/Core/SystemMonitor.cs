using System.Diagnostics;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Polls system metrics on a background timer.
/// Raises SnapshotReady with fresh data every tick.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    public event Action<SystemSnapshot>? SnapshotReady;

    public string GpuSensorDump => _gpus.DebugSensorDump;

    // ACPI thermal zones (the fallback CPU temperature source) need admin; stop asking after this many failures instead of throwing every tick.
    private const int MaxCpuTempFailures = 3;

    private readonly System.Threading.Timer _timer;
    private readonly PerformanceCounter? _cpuTotal, _diskRead, _diskWrite;
    private readonly List<PerformanceCounter> _cpuCores = [];
    private readonly HardwareSensors _sensors = new();
    private readonly GpuMonitor _gpus;
    private readonly DriveHealthMonitor _drives;
    private readonly BatteryMonitor _battery;
    private readonly ProcessWatchdog _watchdog = new();
    private readonly NetworkMeter _network = new();

    // Cached once — CPU name and nominal clock don't change at runtime.
    private string? _cpuName;
    private int _cpuBaseMhz;
    private int _cpuTempFailures;
    private int _polling;
    private volatile bool _disposed;

    public SystemMonitor(int intervalMs)
    {
        _gpus = new GpuMonitor(_sensors);
        _drives = new DriveHealthMonitor(_sensors);
        _battery = new BatteryMonitor(_sensors);
        _cpuTotal = Counter("Processor", "% Processor Time", "_Total", "MONITOR_CPU_INIT");
        try
        {
            var cores = new PerformanceCounterCategory("Processor").GetInstanceNames()
                .Where(n => n != "_Total")
                .OrderBy(n => int.TryParse(n, out var i) ? i : int.MaxValue);
            foreach (var inst in cores)
                if (Counter("Processor", "% Processor Time", inst, $"MONITOR_CPU_CORE_{inst}") is { } c) _cpuCores.Add(c);
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_CPU_CORES", ex); }

        _diskRead = Counter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", "MONITOR_DISK_INIT");
        _diskWrite = Counter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", "MONITOR_DISK_INIT");

        _timer = new System.Threading.Timer(_ => Poll(), null,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(AppSettings.ClampPollInterval(intervalMs)));
    }

    public void SetPollInterval(int ms)
    {
        if (_disposed) return;
        try { _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(AppSettings.ClampPollInterval(ms))); }
        catch (Exception ex) { SessionLog.Write("MONITOR_INTERVAL", ex); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Wait for an in-flight poll so it can't touch counters we're about to dispose.
        using (var done = new ManualResetEvent(false))
        {
            _timer.Dispose(done);
            done.WaitOne(TimeSpan.FromSeconds(2));
        }

        PerformanceCounter?[] all = [_cpuTotal, _diskRead, _diskWrite, .. _cpuCores];
        foreach (var c in all) c?.Dispose();
        _gpus.Dispose();
        _sensors.Dispose();
        _watchdog.Dispose();
    }

    // Creates a primed counter, or null (logged) if it can't be created.
    private static PerformanceCounter? Counter(string category, string name, string instance, string tag)
    {
        try
        {
            var c = new PerformanceCounter(category, name, instance);
            _ = c.NextValue();
            return c;
        }
        catch (Exception ex)
        {
            SessionLog.Write(tag, ex);
            return null;
        }
    }

    private void Poll()
    {
        // Skip overlapping ticks — the counters aren't thread-safe.
        if (_disposed || Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var snap = BuildSnapshot();
            HealthHistory.Instance.Append(snap.HealthScore, snap.Alerts.Count);
            SnapshotReady?.Invoke(snap);
        }
        catch (Exception ex) { SessionLog.Write("MONITOR_POLL", ex); }
        finally { Volatile.Write(ref _polling, 0); }
    }

    private SystemSnapshot BuildSnapshot()
    {
        var snap = new SystemSnapshot
        {
            CpuTotalUsage = NextValue(_cpuTotal),
            CpuCoreUsages = [.. _cpuCores.Select(NextValue)],
            DiskReadMbps = NextValue(_diskRead) / 1_048_576f,
            DiskWriteMbps = NextValue(_diskWrite) / 1_048_576f,
            UptimeSeconds = Environment.TickCount64 / 1000,
        };

        Guarded("MONITOR_SENSORS", _sensors.Update);
        Guarded("MONITOR_CPU_INFO", () => EnrichCpu(snap));
        Guarded("MONITOR_RAM", () => (snap.RamTotalBytes, snap.RamUsedBytes) = MemoryInfo.Read());
        Guarded("MONITOR_NET", () =>
        {
            (snap.NetworkSentMbps, snap.NetworkReceivedMbps) = _network.Read();
            snap.ActiveAdapterName = _network.AdapterName;
        });
        EnrichDisks(snap);
        Guarded("MONITOR_DRIVE_HEALTH", () => snap.PhysicalDisks = _drives.Read());
        Guarded("MONITOR_BATTERY", () => snap.Battery = _battery.Read());
        Guarded("MONITOR_GPU", () => snap.Gpus = _gpus.Read());
        Guarded("MONITOR_PROCS", () => snap.Processes = _watchdog.Read());

        HealthEvaluator.Evaluate(snap, SettingsManager.Current);
        return snap;
    }

    private static float NextValue(PerformanceCounter? c)
    {
        if (c is null) return 0f;
        try { return c.NextValue(); }
        catch (Exception ex)
        {
            SessionLog.Write("MONITOR_COUNTER", ex);
            return 0f;
        }
    }

    private static void Guarded(string tag, Action action)
    {
        try { action(); }
        catch (Exception ex) { SessionLog.Write(tag, ex); }
    }

    private void EnrichCpu(SystemSnapshot snap)
    {
        if (_cpuName is null)
        {
            _cpuName = string.Empty; // set first so a failing query isn't retried every tick
            var (name, mhz) = WmiQuery.Query<(string? name, int mhz)>("SELECT Name, MaxClockSpeed FROM Win32_Processor",
                o => (o["Name"]?.ToString()?.Trim(), Convert.ToInt32(o["MaxClockSpeed"]))).FirstOrDefault();
            _cpuName = name ?? string.Empty;
            _cpuBaseMhz = mhz;
        }
        snap.CpuName = _cpuName;
        snap.CpuBaseSpeedMhz = _cpuBaseMhz;

        // Both sources need elevation; the dashboard shows "N/A — run as admin" when neither reports.
        if (_sensors.ReadCpuTemperature() is { } sensorTemp)
        {
            snap.CpuTempAvailable = true;
            snap.CpuTemperature = sensorTemp;
            return;
        }
        if (_cpuTempFailures >= MaxCpuTempFailures) return;
        try
        {
            var temps = WmiQuery.Query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
                o => Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15, @"root\WMI");
            if (temps.Count == 0) { _cpuTempFailures++; return; }
            snap.CpuTempAvailable = true;
            snap.CpuTemperature = (float)temps.Max();
            _cpuTempFailures = 0;
        }
        catch (Exception ex)
        {
            if (++_cpuTempFailures == MaxCpuTempFailures)
                SessionLog.Write($"[MONITOR] CPU temperature unavailable ({ex.GetType().Name}) — not retrying this session");
        }
    }

    private static void EnrichDisks(SystemSnapshot snap)
    {
        // Local volumes only: network shares can hang IsReady and optical media is always "100% full".
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
            try
            {
                if (!drive.IsReady) continue;
                snap.Disks.Add(new DiskInfo
                {
                    DriveLetter = drive.Name,
                    Label = drive.VolumeLabel,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    DriveFormat = drive.DriveFormat,
                    IsRemovable = drive.DriveType == DriveType.Removable,
                });
            }
            catch (Exception ex) { SessionLog.Write($"[MONITOR] drive {drive.Name}", ex); }
    }
}
