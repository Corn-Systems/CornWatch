using CornWatch.Core;
using CornWatch.Models;
using CornWatch.UI.Tray;

namespace CornWatch.Tests;

public class TrayAndProcessTests
{
    private static SystemSnapshot With(params AlertSeverity[] severities) => new()
    {
        HealthScore = 70,
        Alerts = [.. severities.Select((s, i) => new HealthAlert { Id = $"a{i}", Severity = s })],
    };

    [Fact]
    public void TrayLevelFollowsWorstAlert()
    {
        Assert.Equal(TrayStatusIcon.Level.Ok, TrayStatusIcon.LevelOf(With()));
        Assert.Equal(TrayStatusIcon.Level.Ok, TrayStatusIcon.LevelOf(With(AlertSeverity.Info)));
        Assert.Equal(TrayStatusIcon.Level.Warning, TrayStatusIcon.LevelOf(With(AlertSeverity.Info, AlertSeverity.Warning)));
        Assert.Equal(TrayStatusIcon.Level.Critical, TrayStatusIcon.LevelOf(With(AlertSeverity.Warning, AlertSeverity.Critical)));
    }

    [Fact]
    public void TooltipShowsScoreAndFitsTheShellLimit()
    {
        var tip = TrayStatusIcon.Tooltip(With(AlertSeverity.Warning, AlertSeverity.Critical));
        Assert.Contains("70/100", tip);
        Assert.Contains("2 alerts", tip);
        Assert.True(tip.Length <= 127);
    }

    [Theory]
    [InlineData("csrss", true)]
    [InlineData("LSASS", true)]
    [InlineData("CornWatch", true)]
    [InlineData("chrome", false)]
    public void SystemProcessesAreProtected(string name, bool expected) =>
        Assert.Equal(expected, ProcessActions.IsProtected(name));
}
