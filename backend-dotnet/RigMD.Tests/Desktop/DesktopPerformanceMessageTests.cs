using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public sealed class DesktopPerformanceMessageTests
{
    [Theory]
    [InlineData("completed")]
    [InlineData("incomplete")]
    public void OnlyKnownReadyOutcomesFromTrustedOriginAreAccepted(string outcome)
    {
        var json = "{\"type\":\"rigmd:dashboard-ready\",\"outcome\":\"" + outcome + "\"}";
        Assert.True(DesktopPerformanceMessage.TryReadDashboardReady("http://localhost:5273/", json, out var parsed)); Assert.Equal(outcome, parsed);
        Assert.False(DesktopPerformanceMessage.TryReadDashboardReady("https://evil.test/", json, out _));
        Assert.False(DesktopPerformanceMessage.TryReadDashboardReady("http://localhost:5273.evil.test/", json, out _));
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"type\":true,\"outcome\":\"completed\"}")]
    [InlineData("{\"type\":\"rigmd:dashboard-ready\",\"outcome\":\"repaired\"}")]
    public void MalformedMessagesCannotReportAStartupPass(string json) => Assert.False(DesktopPerformanceMessage.TryReadDashboardReady("http://localhost:5273/", json, out _));
}
