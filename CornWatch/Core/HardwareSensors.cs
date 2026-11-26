using LibreHardwareMonitor.Hardware;

namespace CornWatch.Core;

internal readonly record struct GpuSensorReading(float? TemperatureCelsius, float? FanRpm, float? PowerWatts);

// LifePercent is the drive's remaining rated life (100 = new), from NVMe "Percentage Used" or the SSD's SMART life attribute.
internal readonly record struct StorageSensorReading(string Name, float? TemperatureCelsius, float? LifePercent);

internal readonly record struct BatterySensorReading(
    float? ChargePercent, float? DegradationPercent, float? RateWatts,
    float? DesignedCapacityMWh, float? FullChargeCapacityMWh, float? RemainingSeconds);

/// <summary>
/// Vendor sensor access through LibreHardwareMonitor.
/// GPU temperature / fan / power come from the vendor's user-mode API (AMD ADL,
/// NVIDIA NVAPI, Intel) and need no elevation; neither does the battery. CPU temperature
/// needs a kernel driver and drive SMART data needs raw disk access, so the CPU and
/// storage are only opened when running elevated. If the CPU reports nothing the
/// caller falls back to the ACPI thermal zone; drive health falls back to Windows'
/// own health status (DriveHealthMonitor).
/// </summary>
internal sealed class HardwareSensors : IDisposable
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    private readonly Computer? _computer;
    private readonly List<IHardware> _gpus = [];
    private readonly List<IHardware> _storage = [];
    private readonly IHardware? _cpu, _battery;
    private bool _disposed;

    public HardwareSensors()
    {
        try
        {
            _computer = new Computer
            {
                IsGpuEnabled = true,
                IsBatteryEnabled = true,
                IsCpuEnabled = AppInfo.IsElevated,
                IsStorageEnabled = AppInfo.IsElevated,
            };
            _computer.Open();
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel) _gpus.Add(hardware);
                else if (hardware.HardwareType == HardwareType.Cpu) _cpu ??= hardware;
                else if (hardware.HardwareType == HardwareType.Storage) _storage.Add(hardware);
                else if (hardware.HardwareType == HardwareType.Battery) _battery ??= hardware;
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write("SENSORS_OPEN", ex);
            _computer = null;
        }
    }

    // Refreshes every sensor; call once per poll before reading.
    public void Update()
    {
        if (_disposed) return;
        foreach (var hardware in _gpus) Refresh(hardware);
        if (_cpu is not null) Refresh(_cpu);
        if (_battery is not null) Refresh(_battery);
    }

    // SMART reads are slow, so drives are refreshed on their own, slower schedule.
    public IReadOnlyList<StorageSensorReading> ReadStorage()
    {
        if (_disposed || _storage.Count == 0) return [];
        var readings = new List<StorageSensorReading>();
        foreach (var drive in _storage)
        {
            Refresh(drive);
            var life = drive.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Level && s.Value is not null
                && s.Name.Contains("Life", IgnoreCase))?.Value;
            readings.Add(new StorageSensorReading(drive.Name,
                Pick(drive, SensorType.Temperature, "Composite Temperature", "Temperature"),
                life is { } l ? Math.Clamp(l, 0f, 100f) : null));
        }
        return readings;
    }

    public BatterySensorReading? ReadBattery()
    {
        if (_disposed || _battery is not { } b) return null;
        float? Named(SensorType type, string name) =>
            b.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, IgnoreCase))?.Value;
        var rate = Named(SensorType.Power, "Charge/Discharge Rate") ?? Named(SensorType.Power, "Charge Rate")
                   ?? Named(SensorType.Power, "Discharge Rate");
        return new BatterySensorReading(
            Named(SensorType.Level, "Charge Level"),
            Named(SensorType.Level, "Degradation Level"),
            rate is { } r ? Math.Abs(r) : null,
            Named(SensorType.Energy, "Designed Capacity"),
            Named(SensorType.Energy, "Fully-Charged Capacity"),
            Named(SensorType.TimeSpan, "Remaining Time (Estimated)"));
    }

    // DXGI adapter names in display order → the sensor device for each (null when none matches).
    // Identical cards are paired off in order.
    public IReadOnlyList<object?> MatchGpus(IReadOnlyList<string> adapterNames)
    {
        var unused = new List<IHardware>(_gpus);
        var matches = new List<object?>();
        foreach (var name in adapterNames)
        {
            var match = unused.FirstOrDefault(h => h.Name.Equals(name, IgnoreCase))
                        ?? unused.FirstOrDefault(h => h.Name.Contains(name, IgnoreCase) || name.Contains(h.Name, IgnoreCase));
            if (match is not null) unused.Remove(match);
            matches.Add(match);
        }
        return matches;
    }

    public GpuSensorReading ReadGpu(object? device)
    {
        if (_disposed || device is not IHardware hardware) return default;
        return new GpuSensorReading(
            Pick(hardware, SensorType.Temperature, "GPU Core", "GPU Hot Spot"),
            Pick(hardware, SensorType.Fan),
            Pick(hardware, SensorType.Power, "GPU Package", "GPU Core"));
    }

    public float? ReadCpuTemperature() =>
        _disposed || _cpu is null ? null : Pick(_cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Core Max");

    public string Describe()
    {
        if (_computer is null) return "Sensors: unavailable (see log)";
        var lines = new List<string> { $"Sensors (LibreHardwareMonitor, elevated={AppInfo.IsElevated}):" };
        IEnumerable<IHardware> all = [.. _gpus, .. _cpu is null ? [] : new[] { _cpu }, .. _storage, .. _battery is null ? [] : new[] { _battery }];
        foreach (var hardware in all)
        {
            lines.Add($"  {hardware.Name}");
            lines.AddRange(hardware.Sensors
                .Where(s => s.SensorType is SensorType.Temperature or SensorType.Fan or SensorType.Power or SensorType.Level or SensorType.Energy)
                .Select(s => $"    {s.SensorType} / {s.Name} = {s.Value?.ToString("0.#") ?? "n/a"}"));
        }
        return string.Join("\n", lines);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _computer?.Close(); }
        catch (Exception ex) { SessionLog.Write("SENSORS_CLOSE", ex); }
    }

    private static void Refresh(IHardware hardware)
    {
        try { hardware.Update(); }
        catch (Exception ex) { SessionLog.Write($"SENSORS_UPDATE {hardware.Name}", ex); }
    }

    // First sensor of the type matching a preferred name, else the first sensor of the type that has a value.
    private static float? Pick(IHardware hardware, SensorType type, params string[] preferredNames)
    {
        var sensors = hardware.Sensors.Where(s => s.SensorType == type && s.Value is not null).ToList();
        foreach (var name in preferredNames)
            if (sensors.FirstOrDefault(s => s.Name.Equals(name, IgnoreCase)) is { } preferred) return preferred.Value;
        return sensors.FirstOrDefault()?.Value;
    }
}
