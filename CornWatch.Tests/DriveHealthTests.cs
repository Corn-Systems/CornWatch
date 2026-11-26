using CornWatch.Core;
using CornWatch.Models;

namespace CornWatch.Tests;

public class DriveHealthTests
{
    [Theory]
    [InlineData(0, DriveHealth.Healthy)]
    [InlineData(1, DriveHealth.Warning)]
    [InlineData(2, DriveHealth.Unhealthy)]
    [InlineData(5, DriveHealth.Unknown)]
    [InlineData(42, DriveHealth.Unknown)]
    public void MapsWindowsHealthStatus(int status, DriveHealth expected) =>
        Assert.Equal(expected, DriveHealthMonitor.HealthOf(status));

    [Theory]
    [InlineData(17, 4, "NVMe")]  // NVMe bus wins over media type
    [InlineData(11, 4, "SSD")]
    [InlineData(11, 3, "HDD")]
    [InlineData(7, 0, "Drive")]
    public void NamesTheDriveKind(int bus, int media, string expected) =>
        Assert.Equal(expected, DriveHealthMonitor.KindOf(bus, media));
}
