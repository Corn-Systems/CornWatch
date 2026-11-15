using CornWatch.Core;

namespace CornWatch.Tests;

public class GpuNamingTests
{
    [Fact]
    public void AdapterLuidFormatsLikePerfCounterInstances()
    {
        var adapter = new DxgiAdapter("GPU", 0, 0x00014A30);
        Assert.Equal("luid_0x00000000_0x00014A30", adapter.LuidTag);
    }

    [Fact]
    public void AdapterLuidKeepsHighPart()
    {
        var adapter = new DxgiAdapter("GPU", 0, (0x12L << 32) | 0xABCDEF01);
        Assert.Equal("luid_0x00000012_0xABCDEF01", adapter.LuidTag);
    }

    [Theory]
    [InlineData("pid_10284_luid_0x00000000_0x00014A30_phys_0_eng_0_engtype_3D", "luid_0x00000000_0x00014A30")]
    [InlineData("luid_0x00000000_0x00016FF5_phys_0", "luid_0x00000000_0x00016FF5")]
    [InlineData("pid_1_engtype_3D", null)]
    [InlineData("luid_0x0000", null)]
    public void LuidTagIsExtractedFromInstanceNames(string instance, string? expected) =>
        Assert.Equal(expected, GpuMonitor.LuidTagOf(instance));
}
