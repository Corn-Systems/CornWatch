using System.Runtime.InteropServices;

namespace CornWatch.Core;

internal sealed record DxgiAdapter(string Name, long DedicatedBytes, long Luid)
{
    // Matches the instance naming of the "GPU Engine" / "GPU Adapter Memory" perf counters.
    public string LuidTag => $"luid_0x{(int)(Luid >> 32):X8}_0x{(uint)Luid:X8}";
}

// Enumerates hardware GPUs via DXGI (IDXGIFactory1 → EnumAdapters1 → GetDesc1).
// DedicatedVideoMemory is a SIZE_T, so it reports true VRAM without the WMI 4 GB cap.
internal static class DxgiAdapters
{
    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1")]
    private static extern int CreateDxgiFactory1(ref Guid riid, out IntPtr factory);

    private static readonly Guid _iidFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    // COM vtable slots.
    private const int ReleaseSlot = 2, GetDesc1Slot = 10, EnumAdapters1Slot = 12;
    private const uint SoftwareAdapterFlag = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1Delegate(IntPtr self, ref DxgiAdapterDesc1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1Delegate(IntPtr self, uint index, out IntPtr ppAdapter);

    // Hardware adapters, largest dedicated VRAM first (so a discrete GPU beats an iGPU).
    public static List<DxgiAdapter> Enumerate()
    {
        var adapters = new List<DxgiAdapter>();
        try
        {
            var factoryGuid = _iidFactory1;
            if (CreateDxgiFactory1(ref factoryGuid, out var factory) != 0 || factory == IntPtr.Zero) return adapters;
            try
            {
                var enumAdapters1 = Fn<EnumAdapters1Delegate>(factory, EnumAdapters1Slot);
                for (uint i = 0; enumAdapters1(factory, i, out var adapter) == 0 && adapter != IntPtr.Zero; i++)
                    try
                    {
                        var desc = new DxgiAdapterDesc1();
                        if (Fn<GetDesc1Delegate>(adapter, GetDesc1Slot)(adapter, ref desc) != 0) continue;
                        if ((desc.Flags & SoftwareAdapterFlag) != 0) continue;
                        adapters.Add(new DxgiAdapter(desc.Description.Trim(), (long)desc.DedicatedVideoMemory.ToUInt64(), desc.AdapterLuid));
                    }
                    finally { Release(adapter); }
            }
            finally { Release(factory); }
        }
        catch (Exception ex) { SessionLog.Write("DXGI_ENUM", ex); }

        return [.. adapters.OrderByDescending(a => a.DedicatedBytes)];
    }

    private static T Fn<T>(IntPtr obj, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size));

    private static void Release(IntPtr ptr)
    {
        try { Fn<ReleaseDelegate>(ptr, ReleaseSlot)(ptr); }
        catch { /* best-effort */ }
    }
}
