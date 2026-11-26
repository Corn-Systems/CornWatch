using CornWatch.Core;
using CornWatch.Models;

namespace CornWatch.Tests;

public class HealthEvaluatorTests
{
    private static SystemSnapshot Idle() => new()
    {
        CpuTotalUsage = 10f,
        RamTotalBytes = 100,
        RamUsedBytes = 30,
        Disks = [new DiskInfo { DriveLetter = @"C:\", TotalBytes = 100, FreeBytes = 60 }],
        Gpus = [new GpuInfo { Name = "GPU A", TemperatureCelsius = 50f, VramUsagePercent = 20f }],
    };

    private static SystemSnapshot Evaluated(Action<SystemSnapshot> change)
    {
        var snap = Idle();
        change(snap);
        HealthEvaluator.Evaluate(snap, new AppSettings());
        return snap;
    }

    [Fact]
    public void HealthySystemScoresFullMarksWithNoAlerts()
    {
        var snap = Evaluated(_ => { });
        Assert.Equal(100, snap.HealthScore);
        Assert.Empty(snap.Alerts);
    }

    [Fact]
    public void CpuAboveWarningCostsTenAndRaisesWarning()
    {
        var snap = Evaluated(s => s.CpuTotalUsage = 75f);
        Assert.Equal(90, snap.HealthScore);
        var alert = Assert.Single(snap.Alerts);
        Assert.Equal("cpu.usage", alert.Id);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void CpuAboveCriticalCostsTwentyAndRaisesCritical()
    {
        var snap = Evaluated(s => s.CpuTotalUsage = 95f);
        Assert.Equal(80, snap.HealthScore);
        Assert.Equal(AlertSeverity.Critical, Assert.Single(snap.Alerts).Severity);
    }

    [Fact]
    public void ValueExactlyOnThresholdDoesNotAlert()
    {
        var snap = Evaluated(s => s.CpuTotalUsage = 70f);
        Assert.Empty(snap.Alerts);
    }

    [Fact]
    public void CpuTemperatureIsIgnoredWhenUnavailable()
    {
        var snap = Evaluated(s => { s.CpuTempAvailable = false; s.CpuTemperature = 120f; });
        Assert.Empty(snap.Alerts);
    }

    [Fact]
    public void CpuTemperatureAlertsWhenAvailable()
    {
        var snap = Evaluated(s => { s.CpuTempAvailable = true; s.CpuTemperature = 90f; });
        Assert.Equal(75, snap.HealthScore);
        Assert.Equal("cpu.temp", Assert.Single(snap.Alerts).Id);
    }

    [Fact]
    public void EveryFullDiskGetsItsOwnAlert()
    {
        var snap = Evaluated(s => s.Disks =
        [
            new DiskInfo { DriveLetter = @"C:\", TotalBytes = 100, FreeBytes = 8 },   // 92% → warning
            new DiskInfo { DriveLetter = @"D:\", TotalBytes = 100, FreeBytes = 2 },   // 98% → critical
            new DiskInfo { DriveLetter = @"E:\", TotalBytes = 100, FreeBytes = 50 },
        ]);
        Assert.Equal(100 - 8 - 15, snap.HealthScore);
        Assert.Equal([@"disk.C:\", @"disk.D:\"], snap.Alerts.Select(a => a.Id));
        Assert.Equal(AlertSeverity.Critical, snap.Alerts[1].Severity);
    }

    [Fact]
    public void GpuWithoutTemperatureSensorDoesNotAlert()
    {
        var snap = Evaluated(s => s.Gpus = [new GpuInfo { Name = "GPU A", TemperatureCelsius = null }]);
        Assert.Empty(snap.Alerts);
    }

    [Fact]
    public void EachGpuIsCheckedAndNamedWhenThereAreSeveral()
    {
        var snap = Evaluated(s => s.Gpus =
        [
            new GpuInfo { Name = "GPU A", TemperatureCelsius = 50f },
            new GpuInfo { Name = "GPU B", TemperatureCelsius = 99f },
        ]);
        var alert = Assert.Single(snap.Alerts);
        Assert.Equal("gpu1.temp", alert.Id);
        Assert.StartsWith("GPU B:", alert.Message);
    }

    [Fact]
    public void ScoreNeverGoesBelowZero()
    {
        var snap = Evaluated(s =>
        {
            s.CpuTotalUsage = 100f;
            s.RamUsedBytes = 99;
            s.CpuTempAvailable = true;
            s.CpuTemperature = 110f;
            s.Disks = [.. Enumerable.Range(0, 8).Select(i => new DiskInfo { DriveLetter = $"{(char)('C' + i)}:\\", TotalBytes = 100, FreeBytes = 0 })];
            s.Gpus = [new GpuInfo { TemperatureCelsius = 110f, VramUsagePercent = 99f }];
        });
        Assert.Equal(0, snap.HealthScore);
    }

    [Fact]
    public void ThresholdsComeFromSettings()
    {
        var snap = Idle();
        HealthEvaluator.Evaluate(snap, new AppSettings { RamWarnPercent = 10f, RamCritPercent = 20f });
        Assert.Equal("ram.usage", Assert.Single(snap.Alerts).Id);
        Assert.Equal(AlertSeverity.Critical, snap.Alerts[0].Severity);
    }

    [Fact]
    public void FullRemovableDriveIsNotScored()
    {
        var snap = Evaluated(s => s.Disks.Add(new DiskInfo { DriveLetter = @"E:", TotalBytes = 100, FreeBytes = 0, IsRemovable = true }));
        Assert.Equal(100, snap.HealthScore);
        Assert.Empty(snap.Alerts);
    }
}
