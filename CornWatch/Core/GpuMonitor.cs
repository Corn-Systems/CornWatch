using System.Diagnostics;
using System.Runtime.InteropServices;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Reads GPU metrics using Windows-native APIs.
/// VRAM total is read via DXGI IDXGIAdapter3::QueryVideoMemoryInfo (P/Invoke)
/// which returns the true physical VRAM without the WMI 4GB cap.
/// </summary>
public sealed class gpuMonitor : IDisposable
{
    private const StringComparison ic = StringComparison.OrdinalIgnoreCase;

    private static readonly string[] igpuHints = ["Radeon(TM) Graphics", "Intel", "UHD", "Iris"];

    private readonly List<PerformanceCounter> threeDCounters = [], computeCounters = [], vramCounters = [];
    private readonly string dgpuName;
    private readonly string dgpuLuid;
    private readonly float dgpuVramMb;
    private bool disposed;

    public string debugSensorDump { get; private set; } = string.Empty;

    public gpuMonitor()
    {
        dgpuName = readGpuName();
        dgpuLuid = findDgpuLuid();
        dgpuVramMb = dxgiVram.getDedicatedVramMb(dgpuName);
        initCounters();
    }

    public List<gpuInfo> read()
    {
        var info = new gpuInfo
        {
            name = dgpuName,
            vramTotalMb = dgpuVramMb,
            usagePercent = Math.Min(100f, fold(threeDCounters, (a, b) => a + b, "GPU_READ_3D")),
            computeUsagePercent = fold(computeCounters, Math.Max, "GPU_READ_COMPUTE"),
            vramUsedMb = fold(vramCounters, (a, b) => a + b, "GPU_READ_VRAM") / 1048576f,
        };
        if (info.vramTotalMb > 0)
            info.vramUsagePercent = Math.Min(100f, info.vramUsedMb / info.vramTotalMb * 100f);
        return [info];
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var c in threeDCounters.Concat(computeCounters).Concat(vramCounters)) c.Dispose();
    }

    private static float fold(List<PerformanceCounter> counters, Func<float, float, float> combine, string tag)
    {
        var acc = 0f;
        foreach (var c in counters)
            try { acc = combine(acc, c.NextValue()); }
            catch (Exception ex) { sessionLog.write(tag, ex); }
        return acc;
    }

    private static string readGpuName()
    {
        try
        {
            var names = wmiQuery.query("SELECT Name FROM Win32_VideoController WHERE PNPDeviceID LIKE 'PCI%'",
                    o => o["Name"]?.ToString()?.Trim() ?? string.Empty)
                .Where(n => n.Length > 0 && !n.Contains("Microsoft Basic", ic) && !n.Contains("Virtual", ic))
                .ToList();
            // Prefer a dedicated GPU over an iGPU.
            return names.FirstOrDefault(n => !igpuHints.Any(h => n.Contains(h, ic))) ?? names.LastOrDefault() ?? "GPU";
        }
        catch (Exception ex)
        {
            sessionLog.write("GPU_WMI_NAME", ex);
            return "GPU";
        }
    }

    private string findDgpuLuid()
    {
        try
        {
            var luidCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var luidHasCompute = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var inst in new PerformanceCounterCategory("GPU Engine").GetInstanceNames())
            {
                if (extractLuid(inst) is not { } luid) continue;
                if (inst.Contains("engtype_3D", ic)) luidCounts[luid] = luidCounts.GetValueOrDefault(luid) + 1;
                if (inst.Contains("engtype_Compute", ic)) luidHasCompute.Add(luid);
            }

            debugSensorDump = "LUIDs:\n" + string.Join("\n", luidCounts.Select(kv =>
                $"  {kv.Key}  3D={kv.Value}  compute={luidHasCompute.Contains(kv.Key)}"));
            if (luidCounts.Count == 0) return string.Empty;

            var chosen = luidCounts.Where(kv => kv.Value > 1 && luidHasCompute.Contains(kv.Key))
                             .OrderBy(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault()
                         ?? luidCounts.OrderBy(kv => kv.Value).First().Key;
            debugSensorDump += $"\nChosen: {chosen}";
            return chosen;
        }
        catch (Exception ex)
        {
            debugSensorDump = "LUID scan failed: " + ex.Message;
            sessionLog.write("GPU_LUID", ex);
            return string.Empty;
        }
    }

    private void initCounters()
    {
        if (dgpuLuid.Length == 0) return;

        addCounters("GPU Engine", "Utilization Percentage", inst =>
            inst.Contains("engtype_3D", ic) ? threeDCounters
            : inst.Contains("engtype_Compute", ic) ? computeCounters
            : null);
        addCounters("GPU Process Memory", "Dedicated Usage", _ => vramCounters);

        debugSensorDump += $"\n3D={threeDCounters.Count} Compute={computeCounters.Count} " +
                           $"VRAM-used-counters={vramCounters.Count} VRAM-total={dgpuVramMb:0}MB";
    }

    // Creates a primed counter for every instance of this GPU that `pick` routes to a list.
    private void addCounters(string category, string counter, Func<string, List<PerformanceCounter>?> pick)
    {
        try
        {
            foreach (var inst in new PerformanceCounterCategory(category).GetInstanceNames())
            {
                if (!inst.Contains(dgpuLuid, ic) || pick(inst) is not { } target) continue;
                PerformanceCounter? c = null;
                try
                {
                    c = new PerformanceCounter(category, counter, inst);
                    _ = c.NextValue();
                    target.Add(c);
                }
                catch (Exception ex)
                {
                    c?.Dispose();
                    sessionLog.write($"[GPU] {category} counter '{inst}'", ex);
                }
            }
        }
        catch (Exception ex) { sessionLog.write($"GPU_COUNTERS_{category}", ex); }
    }

    private static string? extractLuid(string inst)
    {
        var parts = inst.Split('_');
        for (var i = 0; i < parts.Length - 3; i++)
            if (parts[i].Equals("luid", ic))
                return parts[i + 1] + "_" + parts[i + 2];
        return null;
    }
}

/// <summary>
/// Uses DXGI via P/Invoke to get true physical dedicated VRAM.
/// IDXGIFactory1 → EnumAdapters1 → IDXGIAdapter3::QueryVideoMemoryInfo
/// This bypasses all WMI 32-bit field limitations.
/// </summary>
internal static class dxgiVram
{
    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
    private static extern int createDxgiFactory1(ref Guid riid, out IntPtr factory);

    private static readonly Guid iidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid iidAdapter3 = new("645967a4-1392-4310-a798-8053ce3e93fd");

    // COM vtable slots.
    private const int querySlot = 0, releaseSlot = 2, getDesc1Slot = 10, enumAdapters1Slot = 12, queryVideoMemoryInfoSlot = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct dxgiQueryVideoMemoryInfo
    {
        public ulong budget, currentUsage, availableForReservation, currentReservation;
    }

    private enum dxgiMemorySegmentGroup { local }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct dxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string description;
        public uint vendorId, deviceId, subSysId, revision;
        public UIntPtr dedicatedVideoMemory, dedicatedSystemMemory, sharedSystemMemory;
        public long adapterLuid;
        public uint flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int queryInterfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppvObject);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int getDesc1Delegate(IntPtr self, ref dxgiAdapterDesc1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint releaseDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int enumAdapters1Delegate(IntPtr self, uint index, out IntPtr ppAdapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int queryVideoMemoryInfoDelegate(IntPtr self, uint nodeIndex, dxgiMemorySegmentGroup group, ref dxgiQueryVideoMemoryInfo info);

    public static float getDedicatedVramMb(string gpuNameHint)
    {
        try
        {
            var factoryGuid = iidFactory1;
            if (createDxgiFactory1(ref factoryGuid, out var factory) != 0 || factory == IntPtr.Zero) return 0f;

            try
            {
                var enumAdapters1 = fn<enumAdapters1Delegate>(factory, enumAdapters1Slot);
                // First-word match works for any vendor ("AMD", "NVIDIA", ...).
                var hint = gpuNameHint.Split(' ')[0];

                for (uint i = 0; enumAdapters1(factory, i, out var adapter) == 0 && adapter != IntPtr.Zero; i++)
                    try
                    {
                        var desc = new dxgiAdapterDesc1();
                        fn<getDesc1Delegate>(adapter, getDesc1Slot)(adapter, ref desc);
                        if (hint.Length > 0 && !desc.description.Contains(hint, StringComparison.OrdinalIgnoreCase)) continue;

                        var mb = queryBudgetMb(adapter);
                        if (mb > 0) return mb;
                    }
                    finally { release(adapter); }
                return 0f;
            }
            finally { release(factory); }
        }
        catch (Exception ex)
        {
            sessionLog.write("DXGI_VRAM", ex);
            return 0f;
        }
    }

    private static float queryBudgetMb(IntPtr adapter)
    {
        var iid = iidAdapter3;
        if (fn<queryInterfaceDelegate>(adapter, querySlot)(adapter, ref iid, out var adapter3) != 0 || adapter3 == IntPtr.Zero)
            return 0f;
        try
        {
            var info = new dxgiQueryVideoMemoryInfo();
            var hr = fn<queryVideoMemoryInfoDelegate>(adapter3, queryVideoMemoryInfoSlot)(adapter3, 0, dxgiMemorySegmentGroup.local, ref info);
            return hr == 0 ? info.budget / 1048576f : 0f;
        }
        finally { release(adapter3); }
    }

    private static T fn<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    private static void release(IntPtr ptr)
    {
        try { fn<releaseDelegate>(ptr, releaseSlot)(ptr); }
        catch { /* best-effort */ }
    }
}
