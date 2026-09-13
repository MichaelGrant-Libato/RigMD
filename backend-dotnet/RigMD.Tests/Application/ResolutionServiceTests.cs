using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class ResolutionServiceTests
{
    private readonly ResolutionService _service = new();

    [Fact]
    public void CheckResolution_ResolvesWorkloadMemoryIssue_WhenFreshScanIsNormal()
    {
        var hardware = CreateHardware(
            ramUsage: 54,
            browserHeavy: false,
            browserMemoryMb: 900);

        var result = _service.CheckResolution(
            "Elevated Memory Pressure From Active Workloads",
            hardware);

        Assert.Equal("resolved", result.resolution_status);
        Assert.Contains("No active issue is detected", result.resolution_summary);
    }

    [Fact]
    public void CheckResolution_KeepsWorkloadMemoryIssueActive_WhenFreshScanStillMatchesProblem()
    {
        var hardware = CreateHardware(
            ramUsage: 84,
            browserHeavy: true,
            browserMemoryMb: 3600);

        var result = _service.CheckResolution(
            "Elevated Memory Pressure From Active Workloads",
            hardware);

        Assert.Equal("still_active", result.resolution_status);
        Assert.Contains("still shows memory pressure", result.resolution_summary);
    }

    [Fact]
    public void CheckResolution_ResolvesElevatedMemoryUsage_WhenMemoryDropsBelowProblemRange()
    {
        var hardware = CreateHardware(
            ramUsage: 62,
            browserHeavy: false,
            browserMemoryMb: 1200);

        var result = _service.CheckResolution(
            "Elevated Memory Usage",
            hardware);

        Assert.Equal("resolved", result.resolution_status);
    }

    [Fact]
    public void CheckResolution_ResolvesNetworkIssue_WhenFreshDnsCheckSucceeds()
    {
        var hardware = CreateHardware(
            ramUsage: 45,
            browserHeavy: false,
            browserMemoryMb: 800,
            dnsResolutionSucceeded: true);

        var result = _service.CheckResolution(
            "Network issue",
            hardware);

        Assert.Equal("resolved", result.resolution_status);
        Assert.Contains("working again", result.resolution_summary);
    }

    private static HardwareProfileDto CreateHardware(
        double ramUsage,
        bool browserHeavy,
        double browserMemoryMb,
        double cpuUsage = 15,
        double storageUsage = 45,
        bool dnsResolutionSucceeded = true)
    {
        return new HardwareProfileDto
        {
            Cpu = new()
            {
                UsagePercent = cpuUsage,
                IsThermallyThrottling = false
            },

            Ram = new()
            {
                TotalGb = 16,
                UsedGb = 16 * (ramUsage / 100),
                UsagePercent = ramUsage
            },

            Network = new()
            {
                HasActiveAdapter = true,
                HasIpv4Address = true,
                HasDefaultGateway = true,
                HasDnsServers = true,
                DnsResolutionSucceeded = dnsResolutionSucceeded
            },

            AllDisks =
            [
                new()
                {
                    Drive = "C:\\",
                    Mountpoint = "C:\\",
                    TotalGb = 512,
                    UsedGb = 512 * (storageUsage / 100),
                    UsagePercent = storageUsage
                }
            ],

            ProcessInsights = new()
            {
                BrowserDetected = true,
                BrowserHeavy = browserHeavy,
                BrowserMemoryMb = browserMemoryMb,
                BrowserProcessCount = browserHeavy ? 24 : 5
            }
        };
    }
}
