namespace CornWatch.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v0.3.0", "0.2.0", true)]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("V1.0", "0.9.9", true)]
    [InlineData("v0.10.0", "0.9.0", true)]      // numeric, not string, comparison
    [InlineData("v0.2.0", "0.2.0", false)]
    [InlineData("v0.1.0", "0.2.0", false)]
    [InlineData("v0.3.0-beta", "0.3.0", false)] // suffix ignored
    [InlineData("v0.3.0+abc123", "0.2.0", true)]
    [InlineData("2", "1.5.0", true)]
    [InlineData("nightly", "0.2.0", false)]     // unparseable tags never count as newer
    [InlineData("", "0.2.0", false)]
    public void IsNewerComparesVersions(string remoteTag, string local, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(remoteTag, local));
}
