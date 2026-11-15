using System.Runtime.InteropServices;

namespace CornWatch.Core;

// Physical memory totals via GlobalMemoryStatusEx — far cheaper than a WMI query every tick.
internal static class MemoryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", EntryPoint = "GlobalMemoryStatusEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    public static (long totalBytes, long usedBytes) Read()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return ((long)status.TotalPhys, (long)(status.TotalPhys - status.AvailPhys));
    }
}
