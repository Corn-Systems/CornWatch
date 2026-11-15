using System.Diagnostics;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Reads load, VRAM and vendor sensors for every hardware GPU, largest VRAM first.
/// Adapters (name, VRAM size, LUID) come from DXGI; load comes from the
/// "GPU Engine" perf category, which has one instance per process per engine.
/// The whole category is re-read every tick so processes that start or exit
/// after launch are counted correctly.
/// </summary>
internal sealed class GpuMonitor : IDisposable
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
    private const string EngineCategory = "GPU Engine", EngineCounter = "Utilization Percentage";
    private const string MemoryCategory = "GPU Adapter Memory", MemoryCounter = "Dedicated Usage";

    private sealed record GpuSlot(DxgiAdapter Adapter, PerformanceCounter? VramUsed, object? SensorDevice);

    private readonly HardwareSensors _sensors;
    private readonly List<GpuSlot> _slots = [];
    private Dictionary<string, CounterSample> _prevSamples = new(StringComparer.OrdinalIgnoreCase);
    private bool _engineReadFailed;
    private bool _disposed;

    public string DebugSensorDump { get; }

    public GpuMonitor(HardwareSensors sensors)
    {
        this._sensors = sensors;
        var adapters = DxgiAdapters.Enumerate();
        var devices = sensors.MatchGpus([.. adapters.Select(a => a.Name)]);
        var memoryInstances = InstanceNames(MemoryCategory);
        for (var i = 0; i < adapters.Count; i++)
            _slots.Add(new GpuSlot(adapters[i], CreateVramCounter(memoryInstances, adapters[i].LuidTag), devices[i]));

        DebugSensorDump = (_slots.Count == 0
            ? "No hardware GPU adapters found via DXGI."
            : "Adapters (DXGI):\n" + string.Join("\n", _slots.Select(s =>
                $"  {s.Adapter.Name}  VRAM={s.Adapter.DedicatedBytes / 1048576f:0} MB  {s.Adapter.LuidTag}  " +
                $"vram-counter={(s.VramUsed is null ? "no" : "ok")}  sensors={(s.SensorDevice is null ? "no" : "ok")}")))
            + "\n" + sensors.Describe();
        SessionLog.Write("[GPU] " + DebugSensorDump.Replace("\n", " | "));
    }

    public List<GpuInfo> Read()
    {
        if (_disposed) return [];

        var loads = ReadEngineLoads();
        var result = new List<GpuInfo>(_slots.Count);
        foreach (var slot in _slots)
        {
            var reading = _sensors.ReadGpu(slot.SensorDevice);
            var info = new GpuInfo
            {
                Name = slot.Adapter.Name,
                VramTotalMb = slot.Adapter.DedicatedBytes / 1048576f,
                TemperatureCelsius = reading.TemperatureCelsius,
                FanRpm = reading.FanRpm,
                PowerWatts = reading.PowerWatts,
            };
            if (loads.TryGetValue(slot.Adapter.LuidTag, out var load))
            {
                info.UsagePercent = Math.Clamp(load.threeD, 0f, 100f);
                info.ComputeUsagePercent = Math.Clamp(load.compute, 0f, 100f);
            }
            if (slot.VramUsed is not null)
                try { info.VramUsedMb = slot.VramUsed.NextValue() / 1048576f; }
                catch (Exception ex) { SessionLog.Write("GPU_READ_VRAM", ex); }
            if (info.VramTotalMb > 0)
                info.VramUsagePercent = Math.Min(100f, info.VramUsedMb / info.VramTotalMb * 100f);
            result.Add(info);
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var slot in _slots) slot.VramUsed?.Dispose();
    }

    // Per adapter (keyed by LUID tag): utilisation summed across every process' 3D / Compute engines.
    private Dictionary<string, (float threeD, float compute)> ReadEngineLoads()
    {
        var loads = new Dictionary<string, (float threeD, float compute)>(StringComparer.OrdinalIgnoreCase);
        if (_engineReadFailed || _slots.Count == 0) return loads;
        try
        {
            var instances = new PerformanceCounterCategory(EngineCategory).ReadCategory()[EngineCounter];
            if (instances is null) return loads;

            var samples = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
            foreach (InstanceData inst in instances.Values)
            {
                var name = inst.InstanceName;
                var is3D = name.Contains("engtype_3D", IgnoreCase);
                if (!is3D && !name.Contains("engtype_Compute", IgnoreCase)) continue;
                if (LuidTagOf(name) is not { } tag) continue;

                samples[name] = inst.Sample;
                if (!_prevSamples.TryGetValue(name, out var prev)) continue;
                var value = CounterSample.Calculate(prev, inst.Sample);
                if (!float.IsFinite(value)) continue;

                var load = loads.GetValueOrDefault(tag);
                loads[tag] = is3D ? (load.threeD + value, load.compute) : (load.threeD, load.compute + value);
            }
            _prevSamples = samples;
        }
        catch (Exception ex)
        {
            // Category missing (very old Windows / no WDDM 2.x driver) — don't retry every tick.
            _engineReadFailed = true;
            SessionLog.Write("GPU_READ_ENGINES", ex);
        }
        return loads;
    }

    // "pid_1234_luid_0x00000000_0x00014A30_phys_0_eng_0_engtype_3D" → "luid_0x00000000_0x00014A30"
    internal static string? LuidTagOf(string instanceName)
    {
        const int tagLength = 26;
        var start = instanceName.IndexOf("luid_0x", IgnoreCase);
        return start < 0 || start + tagLength > instanceName.Length ? null : instanceName.Substring(start, tagLength);
    }

    private static string[] InstanceNames(string category)
    {
        try { return new PerformanceCounterCategory(category).GetInstanceNames(); }
        catch (Exception ex)
        {
            SessionLog.Write($"GPU_INSTANCES {category}", ex);
            return [];
        }
    }

    private static PerformanceCounter? CreateVramCounter(string[] instances, string luidTag)
    {
        try
        {
            var instance = instances.FirstOrDefault(n => n.Contains(luidTag, IgnoreCase));
            return instance is null ? null : new PerformanceCounter(MemoryCategory, MemoryCounter, instance);
        }
        catch (Exception ex)
        {
            SessionLog.Write("GPU_VRAM_COUNTER", ex);
            return null;
        }
    }
}
