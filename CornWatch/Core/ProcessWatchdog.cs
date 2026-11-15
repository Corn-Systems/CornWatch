using System.Diagnostics;

namespace CornWatch.Core;

public class ProcessEntry
{
    public string Name { get; set; } = string.Empty;
    public int Pid { get; set; }
    public int InstanceCount { get; set; } = 1;
    public float CpuPercent { get; set; }
    public long RamBytes { get; set; }
}

/// <summary>
/// Samples the top N process groups by CPU usage.
/// CPU % is calculated from the TotalProcessorTime delta between two
/// consecutive read() calls, normalised by core count so 100% = whole machine.
/// Processes with the same name are grouped (like Task Manager) so 20 chrome
/// instances show as one "chrome (20)" row with summed CPU/RAM.
/// </summary>
public sealed class ProcessWatchdog(int topN = 8) : IDisposable
{
    private readonly int _coreCount = Math.Max(1, Environment.ProcessorCount);

    private Dictionary<int, TimeSpan> _prevCpuTimes = [];
    private DateTime _prevSampleUtc = DateTime.MinValue;
    private bool _disposed;

    public List<ProcessEntry> Read()
    {
        if (_disposed) return [];

        var groups = new Dictionary<string, ProcessEntry>(StringComparer.OrdinalIgnoreCase);
        var nextCpuTimes = new Dictionary<int, TimeSpan>();
        var nowUtc = DateTime.UtcNow;
        var elapsedMs = _prevSampleUtc == DateTime.MinValue ? 0.0 : (nowUtc - _prevSampleUtc).TotalMilliseconds;

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
                            if (elapsedMs > 0 && _prevCpuTimes.TryGetValue(proc.Id, out var prev))
                                cpu = Math.Clamp((float)((total - prev).TotalMilliseconds / elapsedMs / _coreCount * 100.0), 0f, 100f);
                        }
                        catch { /* access denied on protected processes — RAM is still useful */ }

                        var name = proc.ProcessName;
                        var ram = proc.WorkingSet64;
                        if (groups.TryGetValue(name, out var entry))
                        {
                            entry.InstanceCount++;
                            entry.CpuPercent += cpu;
                            entry.RamBytes += ram;
                        }
                        else
                            groups[name] = new ProcessEntry { Name = name, Pid = proc.Id, CpuPercent = cpu, RamBytes = ram };
                    }
                    catch (Exception ex) { SessionLog.Write("PROC_ENTRY", ex); }
        }
        catch (Exception ex) { SessionLog.Write("PROC_READ", ex); }

        _prevCpuTimes = nextCpuTimes;
        _prevSampleUtc = nowUtc;

        return [.. groups.Values.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.RamBytes).Take(topN)];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prevCpuTimes.Clear();
    }
}
