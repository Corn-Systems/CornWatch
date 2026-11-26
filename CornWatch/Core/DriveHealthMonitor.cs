using CornWatch.Models;

namespace CornWatch.Core;

// Physical drive health. Windows' own verdict (MSFT_PhysicalDisk.HealthStatus — the same one
// Settings › Storage › Disks shows) needs no elevation; wear and temperature come from SMART via
// LibreHardwareMonitor and only appear when running as administrator.
internal sealed class DriveHealthMonitor(HardwareSensors sensors)
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
    private const long RefreshMs = 30_000; // drive health changes slowly and SMART reads aren't free

    // MSFT_PhysicalDisk enumerations
    private const int BusUsb = 7, BusSd = 12, BusVirtual = 14, BusFileBackedVirtual = 15, BusNvme = 17;
    private const int MediaHdd = 3, MediaSsd = 4;

    private List<PhysicalDiskInfo> _last = [];
    private long _nextRefresh;
    private bool _wmiUnavailable;

    public List<PhysicalDiskInfo> Read()
    {
        var now = Environment.TickCount64;
        if (now < _nextRefresh) return _last;
        _nextRefresh = now + RefreshMs;

        var disks = QueryWindows();
        var smart = sensors.ReadStorage().ToList();
        foreach (var disk in disks)
        {
            var index = smart.FindIndex(r => r.Name.Equals(disk.Name, IgnoreCase));
            if (index < 0) index = smart.FindIndex(r => r.Name.Contains(disk.Name, IgnoreCase) || disk.Name.Contains(r.Name, IgnoreCase));
            if (index < 0) continue;
            var m = smart[index];
            smart.RemoveAt(index);
            disk.TemperatureCelsius = m.TemperatureCelsius;
            disk.WearPercent = m.LifePercent is { } life ? 100f - life : null;
        }

        // WMI unavailable (stripped Windows / service off): still show what SMART found.
        if (disks.Count == 0)
            disks.AddRange(smart.Select(m => new PhysicalDiskInfo
            {
                Name = m.Name,
                TemperatureCelsius = m.TemperatureCelsius,
                WearPercent = m.LifePercent is { } life ? 100f - life : null,
            }));

        return _last = disks;
    }

    internal static DriveHealth HealthOf(int status) => status switch
    {
        0 => DriveHealth.Healthy,
        1 => DriveHealth.Warning,
        2 => DriveHealth.Unhealthy,
        _ => DriveHealth.Unknown,
    };

    internal static string KindOf(int bus, int media) =>
        bus == BusNvme ? "NVMe" : media == MediaSsd ? "SSD" : media == MediaHdd ? "HDD" : "Drive";

    private List<PhysicalDiskInfo> QueryWindows()
    {
        if (_wmiUnavailable) return [];
        try
        {
            return WmiQuery.Query("SELECT FriendlyName, MediaType, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk",
                    o => (name: o["FriendlyName"]?.ToString()?.Trim() ?? "Disk",
                          media: Convert.ToInt32(o["MediaType"] ?? 0),
                          bus: Convert.ToInt32(o["BusType"] ?? 0),
                          health: Convert.ToInt32(o["HealthStatus"] ?? 5),
                          size: Convert.ToInt64(o["Size"] ?? 0L)),
                    @"root\Microsoft\Windows\Storage")
                .Where(d => d.bus is not (BusVirtual or BusFileBackedVirtual)) // VHDs, Hyper-V, Dev Drive images
                .Select(d => new PhysicalDiskInfo
                {
                    Name = d.name,
                    Kind = KindOf(d.bus, d.media),
                    SizeBytes = d.size,
                    Health = HealthOf(d.health),
                    IsRemovable = d.bus is BusUsb or BusSd,
                })
                .OrderBy(d => d.IsRemovable).ThenBy(d => d.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            _wmiUnavailable = true;
            SessionLog.Write("DRIVE_HEALTH_WMI", ex);
            return [];
        }
    }
}
