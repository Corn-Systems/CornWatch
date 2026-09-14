using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using CornWatch.Models;

namespace CornWatch.Core;

/// <summary>
/// Reads GPU metrics using Windows-native APIs.
/// VRAM total is read via DXGI IDXGIAdapter3::QueryVideoMemoryInfo (P/Invoke)
/// which returns the true physical VRAM without the WMI 4GB cap.
/// </summary>
public sealed class GpuMonitor : IDisposable
{
    private readonly List<PerformanceCounter> _3dCounters      = [];
    private readonly List<PerformanceCounter> _computeCounters = [];
    private readonly List<PerformanceCounter> _videoCounters   = [];
    private readonly List<PerformanceCounter> _vramCounters    = [];
    private          string                   _dgpuLuid        = string.Empty;
    private          string                   _dgpuName        = "GPU";
    private          float                    _dgpuVramMb;
    private          bool                     _disposed;

    public string DebugSensorDump { get; private set; } = string.Empty;

    public GpuMonitor()
    {
        _dgpuName   = ReadGpuNameFromWmi();
        _dgpuLuid   = FindDgpuLuid();
        InitCounters();
        _dgpuVramMb = DxgiVram.GetDedicatedVramMb(_dgpuName);
    }

    private static string ReadGpuNameFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_VideoController WHERE PNPDeviceID LIKE 'PCI%'");
            string fallback = string.Empty;
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("Virtual",         StringComparison.OrdinalIgnoreCase)) continue;
                // Prefer a dedicated GPU over an iGPU — check for common iGPU phrases.
                bool isIgpu = name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Intel",               StringComparison.OrdinalIgnoreCase)
                           || name.Contains("UHD",                 StringComparison.OrdinalIgnoreCase)
                           || name.Contains("Iris",                StringComparison.OrdinalIgnoreCase);
                if (!isIgpu) return name;
                fallback = name;
            }
            if (!string.IsNullOrEmpty(fallback)) return fallback;
        }
        catch (Exception ex) { SessionLog.Write("GPU_WMI_NAME", ex); }
        return "GPU";
    }

    private string FindDgpuLuid()
    {
        try
        {
            var category  = new PerformanceCounterCategory("GPU Engine");
            var instances = category.GetInstanceNames();

            var luidCounts     = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var luidHasCompute = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var inst in instances)
            {
                var luid = ExtractLuid(inst);
                if (luid is null) continue;
                if (inst.Contains("engtype_3D",      StringComparison.OrdinalIgnoreCase))
                    luidCounts[luid] = luidCounts.GetValueOrDefault(luid) + 1;
                if (inst.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                    luidHasCompute.Add(luid);
            }

            DebugSensorDump = "LUIDs:\n" + string.Join("\n", luidCounts.Select(kv =>
                $"  {kv.Key}  3D={kv.Value}  compute={luidHasCompute.Contains(kv.Key)}"));

            if (luidCounts.Count > 0)
            {
                var candidates = luidCounts
                    .Where(kv => kv.Value > 1 && luidHasCompute.Contains(kv.Key))
                    .OrderBy(kv => kv.Value)
                    .ToList();

                var chosen = candidates.Count > 0
                    ? candidates.First().Key
                    : luidCounts.OrderBy(kv => kv.Value).First().Key;

                DebugSensorDump += $"\nChosen: {chosen}";
                return chosen;
            }
        }
        catch (Exception ex) { DebugSensorDump = "LUID scan failed: " + ex.Message; SessionLog.Write("GPU_LUID", ex); }
        return string.Empty;
    }

    private void InitCounters()
    {
        if (string.IsNullOrEmpty(_dgpuLuid)) return;
        try
        {
            var engCat = new PerformanceCounterCategory("GPU Engine");
            foreach (var inst in engCat.GetInstanceNames())
            {
                if (!inst.Contains(_dgpuLuid, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                    _ = c.NextValue();
                    if      (inst.Contains("engtype_3D",      StringComparison.OrdinalIgnoreCase)) _3dCounters.Add(c);
                    else if (inst.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase)) _computeCounters.Add(c);
                    else if (inst.Contains("engtype_Video",   StringComparison.OrdinalIgnoreCase)) _videoCounters.Add(c);
                }
                catch (Exception ex) { SessionLog.Write($"[GPU] engine counter '{inst}'", ex); }
            }

            try
            {
                var memCat = new PerformanceCounterCategory("GPU Process Memory");
                foreach (var inst in memCat.GetInstanceNames())
                {
                    if (!inst.Contains(_dgpuLuid, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var c = new PerformanceCounter("GPU Process Memory", "Dedicated Usage", inst);
                        _ = c.NextValue();
                        _vramCounters.Add(c);
                    }
                    catch (Exception ex) { SessionLog.Write($"[GPU] VRAM counter '{inst}'", ex); }
                }
            }
            catch (Exception ex) { SessionLog.Write("GPU_VRAM_COUNTERS", ex); }

            DebugSensorDump += $"\n3D={_3dCounters.Count} Compute={_computeCounters.Count} " +
                               $"VRAM-used-counters={_vramCounters.Count} " +
                               $"VRAM-total={_dgpuVramMb:0}MB (set after init)";
        }
        catch (Exception ex) { SessionLog.Write("GPU_COUNTER_INIT", ex); }
    }

    public List<GpuInfo> Read()
    {
        var info = new GpuInfo { Name = _dgpuName, VramTotalMb = _dgpuVramMb };

        float load = 0f;
        foreach (var c in _3dCounters)
            try { load += c.NextValue(); }
            catch (Exception ex) { SessionLog.Write("GPU_READ_3D", ex); }
        info.UsagePercent = Math.Min(100f, load);

        float compute = 0f;
        foreach (var c in _computeCounters)
            try { compute = Math.Max(compute, c.NextValue()); }
            catch (Exception ex) { SessionLog.Write("GPU_READ_COMPUTE", ex); }
        info.ComputeUsagePercent = compute;

        float vramBytes = 0f;
        foreach (var c in _vramCounters)
            try { vramBytes += c.NextValue(); }
            catch (Exception ex) { SessionLog.Write("GPU_READ_VRAM", ex); }
        info.VramUsedMb = vramBytes / 1024f / 1024f;

        if (info.VramTotalMb > 0)
            info.VramUsagePercent = Math.Min(100f, info.VramUsedMb / info.VramTotalMb * 100f);

        return [info];
    }

    private static string? ExtractLuid(string inst)
    {
        var parts = inst.Split('_');
        for (int i = 0; i < parts.Length - 3; i++)
            if (parts[i].Equals("luid", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1] + "_" + parts[i + 2];
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var c in _3dCounters.Concat(_computeCounters).Concat(_videoCounters).Concat(_vramCounters))
            c.Dispose();
    }
}

/// <summary>
/// Uses DXGI via P/Invoke to get true physical dedicated VRAM.
/// IDXGIFactory1 → EnumAdapters1 → IDXGIAdapter3::QueryVideoMemoryInfo
/// This bypasses all WMI 32-bit field limitations.
/// </summary>
internal static class DxgiVram
{
    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIAdapter3 = new("645967a4-1392-4310-a798-8053ce3e93fd");

    private const int EnumAdapters1Slot        = 12;
    private const int GetDesc1Slot             = 10;
    private const int QueryVideoMemoryInfoSlot = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget, CurrentUsage, AvailableForReservation, CurrentReservation;
    }

    private enum DXGI_MEMORY_SEGMENT_GROUP { Local = 0, NonLocal = 1 }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long    AdapterLuid;
        public uint    Flags;
    }

    public static float GetDedicatedVramMb(string gpuNameHint)
    {
        try
        {
            var factoryGuid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref factoryGuid, out var factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
                return 0f;

            try
            {
                var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(factoryPtr), EnumAdapters1Slot * IntPtr.Size));

                uint idx = 0;
                while (true)
                {
                    int hr = enumAdapters1(factoryPtr, idx++, out var adapterPtr);
                    if (hr != 0 || adapterPtr == IntPtr.Zero) break;
                    try
                    {
                        var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                            Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapterPtr), GetDesc1Slot * IntPtr.Size));
                        var desc = new DXGI_ADAPTER_DESC1();
                        getDesc1(adapterPtr, ref desc);

                        // Skip adapters that don't match our target GPU name.
                        // The previous check was AMD-specific ("RX"); use a
                        // broader first-word match that works for any vendor.
                        bool nameMatches = string.IsNullOrEmpty(gpuNameHint)
                            || desc.Description.Contains(
                                gpuNameHint.Split(' ')[0], StringComparison.OrdinalIgnoreCase);

                        if (!nameMatches)
                        {
                            Release(adapterPtr);
                            continue;
                        }

                        var adapter3Guid = IID_IDXGIAdapter3;
                        var qi = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(
                            Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapterPtr), 0));
                        if (qi(adapterPtr, ref adapter3Guid, out var adapter3Ptr) == 0 && adapter3Ptr != IntPtr.Zero)
                        {
                            try
                            {
                                var qvmi = Marshal.GetDelegateForFunctionPointer<QueryVideoMemoryInfoDelegate>(
                                    Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter3Ptr), QueryVideoMemoryInfoSlot * IntPtr.Size));
                                var info = new DXGI_QUERY_VIDEO_MEMORY_INFO();
                                if (qvmi(adapter3Ptr, 0, DXGI_MEMORY_SEGMENT_GROUP.Local, ref info) == 0 && info.Budget > 0)
                                    return info.Budget / 1024f / 1024f;
                            }
                            finally { Release(adapter3Ptr); }
                        }
                    }
                    finally { Release(adapterPtr); }
                }
                return 0f;
            }
            finally { Release(factoryPtr); }
        }
        catch (Exception ex) { SessionLog.Write("DXGI_VRAM", ex); return 0f; }
    }

    private static void Release(IntPtr ptr)
    {
        try
        {
            var rel = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(
                Marshal.ReadIntPtr(Marshal.ReadIntPtr(ptr), 2 * IntPtr.Size));
            rel(ptr);
        }
        catch { }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int  QueryInterfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppvObject);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int  GetDesc1Delegate(IntPtr self, ref DXGI_ADAPTER_DESC1 pDesc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int  EnumAdapters1Delegate(IntPtr self, uint index, out IntPtr ppAdapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int  QueryVideoMemoryInfoDelegate(IntPtr self, uint nodeIndex, DXGI_MEMORY_SEGMENT_GROUP group, ref DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
}
