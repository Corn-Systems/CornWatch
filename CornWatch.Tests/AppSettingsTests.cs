namespace CornWatch.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData(0, 250)]
    [InlineData(249, 250)]
    [InlineData(1000, 1000)]
    [InlineData(999_999, 30_000)]
    public void PollIntervalIsClamped(int input, int expected)
    {
        var settings = new AppSettings { PollIntervalMs = input };
        settings.Normalize();
        Assert.Equal(expected, settings.PollIntervalMs);
    }

    [Fact]
    public void DefaultsSurviveNormalizeUnchanged()
    {
        var settings = new AppSettings();
        settings.Normalize();
        var defaults = new AppSettings();
        foreach (var prop in typeof(AppSettings).GetProperties())
            Assert.Equal(prop.GetValue(defaults), prop.GetValue(settings));
    }

    [Fact]
    public void WarningIsPulledDownToCritical()
    {
        var settings = new AppSettings { CpuWarnPercent = 95f, CpuCritPercent = 80f };
        settings.Normalize();
        Assert.Equal(80f, settings.CpuWarnPercent);
        Assert.Equal(80f, settings.CpuCritPercent);
    }

    [Fact]
    public void PercentagesAreCappedAtOneHundredAndTemperaturesAtOneFifty()
    {
        var settings = new AppSettings { RamCritPercent = 400f, GpuCritTempC = 900f, DiskWarnPercent = -5f };
        settings.Normalize();
        Assert.Equal(100f, settings.RamCritPercent);
        Assert.Equal(150f, settings.GpuCritTempC);
        Assert.Equal(1f, settings.DiskWarnPercent);
    }

    [Fact]
    public void NonFiniteValuesFallBackToSafeLimits()
    {
        var settings = new AppSettings { CpuWarnTempC = float.NaN, CpuCritTempC = float.PositiveInfinity };
        settings.Normalize();
        Assert.Equal(150f, settings.CpuCritTempC);
        Assert.Equal(150f, settings.CpuWarnTempC);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(85, 85)]
    [InlineData(250, 100)]
    public void OverlayOpacityIsClamped(int input, int expected)
    {
        var settings = new AppSettings { OverlayOpacityPercent = input };
        settings.Normalize();
        Assert.Equal(expected, settings.OverlayOpacityPercent);
    }

    [Theory]
    [InlineData("bottomleft", "BottomLeft")]
    [InlineData("TopRight", "TopRight")]
    [InlineData("middle", "TopRight")]
    public void OverlayCornerIsNormalised(string input, string expected)
    {
        var settings = new AppSettings { OverlayCorner = input };
        settings.Normalize();
        Assert.Equal(expected, settings.OverlayCorner);
    }
}
