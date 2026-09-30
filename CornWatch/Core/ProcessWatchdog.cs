using System.Diagnostics;

namespace CornWatch.Core;

public class processEntry
{
    public string name { get; set; } = string.Empty;
    public int pid { get; set; }
    public int instanceCount { get; set; } = 1;
    public float cpuPercent { get; set; }
    public long ramBytes { get; set; }
}

/// <summary>
/// Samples the top N process groups by CPU usage.
/// CPU % is calculated from the TotalProcessorTime delta between two
/// consecutive read() calls, normalised by core count so 100% = whole machine.
/// Processes with the same name are grouped (like Task Manager) so 20 chrome
/// instances show as one "chrome (20)" row with summed CPU/RAM.
/// </summary>
public sealed class processWatchdog(int topN = 8) : IDisposable
{
    private readonly int coreCount = Math.Max(1, Environment.ProcessorCount);

    private Dictionary<int, TimeSpan> prevCpuTimes = [];
    private DateTime prevSampleUtc = DateTime.MinValue;
    private bool disposed;

    public List<processEntry> read()
    {
        if (disposed) return [];

        var groups = new Dictionary<string, processEntry>(StringComparer.OrdinalIgnoreCase);
        var nextCpuTimes = new Dictionary<int, TimeSpan>();
        var nowUtc = DateTime.UtcNow;
        var elapsedMs = prevSampleUtc == DateTime.MinValue ? 0.0 : (nowUtc - prevSampleUtc).TotalMilliseconds;

        try
        {
            foreach (var proc in Process.GetProcesses())
                using (proc)
                    try
                    {
                        if (proc.Id <= 4) continue; // skip System / Idle

                        var cpu = 0f;
                        try
                        {
                            var total = proc.TotalProcessorTime;
                            nextCpuTimes[proc.Id] = total;
                            if (elapsedMs > 0 && prevCpuTimes.TryGetValue(proc.Id, out var prev))
                                cpu = Math.Clamp((float)((total - prev).TotalMilliseconds / elapsedMs / coreCount * 100.0), 0f, 100f);
                        }
                        catch { /* access denied on protected processes — RAM is still useful */ }

                        var name = proc.ProcessName;
                        var ram = proc.WorkingSet64;
                        if (groups.TryGetValue(name, out var entry))
                        {
                            entry.instanceCount++;
                            entry.cpuPercent += cpu;
                            entry.ramBytes += ram;
                        }
                        else
                            groups[name] = new processEntry { name = name, pid = proc.Id, cpuPercent = cpu, ramBytes = ram };
                    }
                    catch (Exception ex) { sessionLog.write("PROC_ENTRY", ex); }
        }
        catch (Exception ex) { sessionLog.write("PROC_READ", ex); }

        prevCpuTimes = nextCpuTimes;
        prevSampleUtc = nowUtc;

        return [.. groups.Values.OrderByDescending(p => p.cpuPercent).ThenByDescending(p => p.ramBytes).Take(topN)];
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        prevCpuTimes.Clear();
    }
}
