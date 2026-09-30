using System.Diagnostics;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Polls system metrics on a background timer.
/// Raises snapshotReady with fresh data every tick.
/// </summary>
public sealed class systemMonitor : IDisposable
{
    public event Action<systemSnapshot>? snapshotReady;

    public int pollIntervalMs { get; private set; }
    public string gpuSensorDump => gpus.debugSensorDump;

    private readonly System.Threading.Timer timer;
    private readonly PerformanceCounter? cpuTotal, diskRead, diskWrite, netSent, netRecv;
    private readonly List<PerformanceCounter> cpuCores = [];
    private readonly gpuMonitor gpus = new();
    private readonly processWatchdog watchdog = new();
    private readonly string adapterName;

    // Cached once — CPU name and base clock don't change at runtime.
    private string cpuName = string.Empty;
    private int cpuBaseMhz;
    private int polling;
    private bool disposed;

    public systemMonitor(int intervalMs = 1000)
    {
        pollIntervalMs = Math.Clamp(intervalMs, 250, 30_000);

        cpuTotal = counter("Processor", "% Processor Time", "_Total", "MONITOR_CPU_INIT");
        try
        {
            var cores = new PerformanceCounterCategory("Processor").GetInstanceNames()
                .Where(n => n != "_Total")
                .OrderBy(n => int.TryParse(n, out var i) ? i : int.MaxValue);
            foreach (var inst in cores)
                if (counter("Processor", "% Processor Time", inst, $"MONITOR_CPU_CORE_{inst}") is { } c) cpuCores.Add(c);
        }
        catch (Exception ex) { sessionLog.write("MONITOR_CPU_CORES", ex); }

        diskRead = counter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", "MONITOR_DISK_INIT");
        diskWrite = counter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", "MONITOR_DISK_INIT");

        adapterName = pickNetworkAdapter();
        if (adapterName.Length > 0)
        {
            netSent = counter("Network Interface", "Bytes Sent/sec", adapterName, "MONITOR_NET_INIT");
            netRecv = counter("Network Interface", "Bytes Received/sec", adapterName, "MONITOR_NET_INIT");
        }

        timer = new System.Threading.Timer(_ => poll(), null,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(pollIntervalMs));
    }

    public void setPollInterval(int ms)
    {
        if (disposed || ms < 250) return;
        pollIntervalMs = ms;
        try { timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(ms)); }
        catch (Exception ex) { sessionLog.write("MONITOR_INTERVAL", ex); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        // Wait for an in-flight poll so it can't touch counters we're about to dispose.
        using (var done = new ManualResetEvent(false))
        {
            timer.Dispose(done);
            done.WaitOne(TimeSpan.FromSeconds(2));
        }

        PerformanceCounter?[] all = [cpuTotal, diskRead, diskWrite, netSent, netRecv, .. cpuCores];
        foreach (var c in all) c?.Dispose();
        gpus.Dispose();
        watchdog.Dispose();
    }

    // Creates a primed counter, or null (logged) if it can't be created.
    private static PerformanceCounter? counter(string category, string name, string instance, string tag)
    {
        try
        {
            var c = new PerformanceCounter(category, name, instance);
            _ = c.NextValue();
            return c;
        }
        catch (Exception ex)
        {
            sessionLog.write(tag, ex);
            return null;
        }
    }

    private static string pickNetworkAdapter()
    {
        try
        {
            string[] virtualHints = ["loopback", "isatap", "teredo", "bluetooth", "virtual", "vmware", "vbox", "vethernet", "tunnel", "tap-"];
            var instances = new PerformanceCounterCategory("Network Interface").GetInstanceNames();

            // Prefer a non-virtual adapter, then anything that isn't loopback, then whatever exists.
            var chosen = instances.FirstOrDefault(n => !virtualHints.Any(v => n.Contains(v, StringComparison.OrdinalIgnoreCase)))
                         ?? instances.FirstOrDefault(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
                         ?? instances.FirstOrDefault();

            sessionLog.write(chosen is null
                ? "[MONITOR] No network adapters found in perf counter category."
                : $"[MONITOR] Selected network adapter: {chosen}");
            return chosen ?? string.Empty;
        }
        catch (Exception ex)
        {
            sessionLog.write("MONITOR_NET_ADAPTER", ex);
            return string.Empty;
        }
    }

    private void poll()
    {
        // Skip overlapping ticks — the counters aren't thread-safe.
        if (disposed || Interlocked.Exchange(ref polling, 1) == 1) return;
        try
        {
            var snap = buildSnapshot();
            healthHistory.instance.append(snap.healthScore, snap.alerts.Count);
            snapshotReady?.Invoke(snap);
        }
        catch (Exception ex) { sessionLog.write("MONITOR_POLL", ex); }
        finally { Volatile.Write(ref polling, 0); }
    }

    private systemSnapshot buildSnapshot()
    {
        var snap = new systemSnapshot
        {
            cpuTotalUsage = nextValue(cpuTotal),
            cpuCoreUsages = [.. cpuCores.Select(nextValue)],
            diskReadMbps = nextValue(diskRead) / 1_048_576f,
            diskWriteMbps = nextValue(diskWrite) / 1_048_576f,
            networkSentMbps = nextValue(netSent) / 1_048_576f,
            networkReceivedMbps = nextValue(netRecv) / 1_048_576f,
            activeAdapterName = adapterName,
            uptimeSeconds = Environment.TickCount64 / 1000,
        };

        guarded("MONITOR_WMI_CPU", () => enrichCpu(snap));
        guarded("MONITOR_WMI_RAM", () => enrichRam(snap));
        enrichDisks(snap);
        guarded("MONITOR_GPU", () => snap.gpus = gpus.read());
        guarded("MONITOR_PROCS", () => snap.processes = watchdog.read());

        var cfg = settingsManager.current;
        snap.healthScore = healthScore(snap, cfg);
        snap.alerts = alerts(snap, cfg);
        return snap;
    }

    private static float nextValue(PerformanceCounter? c)
    {
        if (c is null) return 0f;
        try { return c.NextValue(); }
        catch (Exception ex)
        {
            sessionLog.write("MONITOR_COUNTER", ex);
            return 0f;
        }
    }

    private static void guarded(string tag, Action action)
    {
        try { action(); }
        catch (Exception ex) { sessionLog.write(tag, ex); }
    }

    private void enrichCpu(systemSnapshot snap)
    {
        if (cpuName.Length == 0)
        {
            var (name, mhz) = wmiQuery.query<(string? name, int mhz)>("SELECT Name, CurrentClockSpeed FROM Win32_Processor",
                o => (o["Name"]?.ToString()?.Trim(), Convert.ToInt32(o["CurrentClockSpeed"]))).FirstOrDefault();
            cpuName = name ?? string.Empty;
            cpuBaseMhz = mhz;
        }
        snap.cpuName = cpuName;
        snap.cpuBaseSpeedMhz = cpuBaseMhz;

        // Requires admin; the dashboard shows "N/A (run as admin)" when unavailable.
        try
        {
            var temps = wmiQuery.query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
                o => Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15, @"root\WMI");
            snap.cpuTempAvailable = temps.Count > 0;
            if (temps.Count > 0) snap.cpuTemperature = (float)temps[0];
        }
        catch { /* not logged — fires every tick for standard users and would spam the log */ }
    }

    private static void enrichRam(systemSnapshot snap)
    {
        var mem = wmiQuery.query("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem",
            o => (total: Convert.ToInt64(o["TotalVisibleMemorySize"]) * 1024L, free: Convert.ToInt64(o["FreePhysicalMemory"]) * 1024L));
        if (mem.Count == 0) return;
        snap.ramTotalBytes = mem[0].total;
        snap.ramUsedBytes = mem[0].total - mem[0].free;
    }

    private static void enrichDisks(systemSnapshot snap)
    {
        foreach (var drive in DriveInfo.GetDrives())
            try
            {
                if (!drive.IsReady) continue;
                snap.disks.Add(new diskInfo
                {
                    driveLetter = drive.Name,
                    label = drive.VolumeLabel,
                    totalBytes = drive.TotalSize,
                    freeBytes = drive.TotalFreeSpace,
                    driveFormat = drive.DriveFormat,
                });
            }
            catch (Exception ex) { sessionLog.write($"[MONITOR] drive {drive.Name}", ex); }
    }

    private static int penalty(float value, float warn, float crit, int warnPoints, int critPoints) =>
        value > crit ? critPoints : value > warn ? warnPoints : 0;

    private static int healthScore(systemSnapshot s, appSettings c)
    {
        var score = 100
            - penalty(s.cpuTotalUsage, c.cpuWarnPercent, c.cpuCritPercent, 10, 20)
            - penalty(s.ramUsagePercent, c.ramWarnPercent, c.ramCritPercent, 10, 20)
            - s.disks.Sum(d => penalty(d.usagePercent, c.diskWarnPercent, c.diskCritPercent, 8, 15));

        if (s.cpuTempAvailable) score -= penalty(s.cpuTemperature, c.cpuWarnTempC, c.cpuCritTempC, 10, 25);
        if (s.primaryGpu is { } g)
            score -= penalty(g.temperatureCelsius, c.gpuWarnTempC, c.gpuCritTempC, 10, 25)
                   + penalty(g.vramUsagePercent, c.gpuVramWarnPercent, c.gpuVramCritPercent, 7, 15);

        return Math.Max(0, score);
    }

    private static List<healthAlert> alerts(systemSnapshot s, appSettings c)
    {
        var list = new List<healthAlert>();
        void add(alertSeverity severity, string category, string message) =>
            list.Add(new() { severity = severity, category = category, message = message });

        if (s.cpuTotalUsage > c.cpuCritPercent) add(alertSeverity.critical, "CPU", $"CPU at {s.cpuTotalUsage:0}% — unusually high");
        else if (s.cpuTotalUsage > c.cpuWarnPercent) add(alertSeverity.warning, "CPU", $"CPU at {s.cpuTotalUsage:0}%");

        if (s.cpuTempAvailable && s.cpuTemperature > c.cpuCritTempC) add(alertSeverity.critical, "CPU", $"CPU temp {s.cpuTemperature:0}°C — consider cooling");
        else if (s.cpuTempAvailable && s.cpuTemperature > c.cpuWarnTempC) add(alertSeverity.warning, "CPU", $"CPU temp {s.cpuTemperature:0}°C");

        if (s.ramUsagePercent > c.ramCritPercent) add(alertSeverity.critical, "RAM", $"RAM at {s.ramUsagePercent:0}% — close some apps");

        foreach (var d in s.disks.Where(d => d.usagePercent > c.diskWarnPercent))
            add(alertSeverity.warning, "Disk", $"{d.driveLetter} is {d.usagePercent:0}% full");

        if (s.primaryGpu is { } g)
        {
            if (g.temperatureCelsius > c.gpuCritTempC) add(alertSeverity.critical, "GPU", $"GPU temp {g.temperatureCelsius:0}°C — critical!");
            else if (g.temperatureCelsius > c.gpuWarnTempC) add(alertSeverity.warning, "GPU", $"GPU temp {g.temperatureCelsius:0}°C — running warm");

            if (g.vramUsagePercent > c.gpuVramCritPercent)
                add(alertSeverity.critical, "GPU", $"VRAM nearly full ({g.vramUsedMb:0}/{g.vramTotalMb:0} MB)");
        }

        return list;
    }
}
