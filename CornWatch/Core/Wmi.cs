using System.Management;

namespace CornWatch.Core;

internal static class wmiQuery
{
    // Runs a WQL query and projects each row, disposing every WMI object.
    public static List<T> query<T>(string wql, Func<ManagementObject, T> select, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var rows = new List<T>();
        foreach (ManagementObject row in results)
            using (row) rows.Add(select(row));
        return rows;
    }
}
