using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public sealed class HomeSummaryMessageTests
{
    [Fact]
    public void SummaryReadinessHasItsOwnTrustedMessageAndCannotBecomeEssentialStartup()
    {
        const string json = "{\"type\":\"rigmd:home-summary-ready\",\"outcome\":\"completed\"}";
        Assert.True(DesktopPerformanceMessage.TryReadHomeSummaryReady("http://localhost:5273/", json, out var outcome));
        Assert.Equal("completed", outcome);
        Assert.False(DesktopPerformanceMessage.TryReadDashboardReady("http://localhost:5273/", json, out _));
        Assert.False(DesktopPerformanceMessage.TryReadHomeSummaryReady("https://untrusted.example/", json, out _));
        Assert.False(DesktopPerformanceMessage.TryReadHomeSummaryReady("http://localhost:5273/", json.Replace("completed", "success"), out _));
    }
}
