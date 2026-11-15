using CornWatch.Models;

namespace CornWatch.Core;

// Turns a snapshot into a 0–100 health score plus alerts. Every check feeds both,
// so anything that costs score is also explained in the alert list.
internal static class HealthEvaluator
{
    public static void Evaluate(SystemSnapshot s, AppSettings c)
    {
        var score = 100;
        var alerts = new List<HealthAlert>();

        void Check(string id, string category, float value, float warn, float crit,
            int warnPoints, int critPoints, string warnMessage, string critMessage)
        {
            var critical = value > crit;
            if (!critical && value <= warn) return;
            score -= critical ? critPoints : warnPoints;
            alerts.Add(new HealthAlert
            {
                Id = id,
                Category = category,
                Severity = critical ? AlertSeverity.Critical : AlertSeverity.Warning,
                Message = critical ? critMessage : warnMessage,
            });
        }

        Check("cpu.usage", "CPU", s.CpuTotalUsage, c.CpuWarnPercent, c.CpuCritPercent, 10, 20,
            $"CPU at {s.CpuTotalUsage:0}%", $"CPU at {s.CpuTotalUsage:0}% — unusually high");

        if (s.CpuTempAvailable)
            Check("cpu.temp", "CPU", s.CpuTemperature, c.CpuWarnTempC, c.CpuCritTempC, 10, 25,
                $"CPU temp {s.CpuTemperature:0}°C", $"CPU temp {s.CpuTemperature:0}°C — consider cooling");

        Check("ram.usage", "RAM", s.RamUsagePercent, c.RamWarnPercent, c.RamCritPercent, 10, 20,
            $"RAM at {s.RamUsagePercent:0}%", $"RAM at {s.RamUsagePercent:0}% — close some apps");

        foreach (var d in s.Disks)
            Check("disk." + d.DriveLetter, "Disk", d.UsagePercent, c.DiskWarnPercent, c.DiskCritPercent, 8, 15,
                $"{d.DriveLetter} is {d.UsagePercent:0}% full", $"{d.DriveLetter} is {d.UsagePercent:0}% full — almost out of space");

        for (var i = 0; i < s.Gpus.Count; i++)
        {
            var g = s.Gpus[i];
            // Name the card only when there is more than one to tell apart.
            var who = s.Gpus.Count > 1 ? $"{g.Name}: " : string.Empty;
            if (g.TemperatureCelsius is { } temp)
                Check($"gpu{i}.temp", "GPU", temp, c.GpuWarnTempC, c.GpuCritTempC, 10, 25,
                    $"{who}GPU temp {temp:0}°C — running warm", $"{who}GPU temp {temp:0}°C — critical!");
            Check($"gpu{i}.vram", "GPU", g.VramUsagePercent, c.GpuVramWarnPercent, c.GpuVramCritPercent, 7, 15,
                $"{who}VRAM at {g.VramUsagePercent:0}%", $"{who}VRAM nearly full ({g.VramUsedMb:0}/{g.VramTotalMb:0} MB)");
        }

        s.HealthScore = Math.Max(0, score);
        s.Alerts = alerts;
    }
}
