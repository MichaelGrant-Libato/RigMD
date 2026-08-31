using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class AutomaticDiagnosisServiceTests
{
    private readonly AutomaticDiagnosisService _service = new();

    [Fact]
    public void Diagnose_ReturnsHighMemoryPressure_WhenRamIsVeryHigh()
    {
        var input = CreateInput(
            ramUsage: 92,
            browserHeavy: false,
            browserMemoryMb: 1200,
            diskUsage: 45,
            mode: "full");

        var result = _service.Diagnose(input);

        Assert.Equal("High Memory Pressure", result.DiagnosedCategory);
        Assert.Equal("Troubleshoot", result.ActionCategory);
        Assert.Equal("High", result.ConfidenceLabel);
    }

    [Fact]
    public void Diagnose_ReturnsWorkloadPressure_WhenRamAndBrowserAreElevated()
    {
        var input = CreateInput(
            ramUsage: 86,
            browserHeavy: true,
            browserMemoryMb: 4800,
            diskUsage: 45,
            mode: "scenario",
            scenarioId: "slow-system");

        var result = _service.Diagnose(input);

        Assert.Equal(
            "Elevated Memory Pressure From Active Workloads",
            result.DiagnosedCategory);

        Assert.Equal("Troubleshoot", result.ActionCategory);
        Assert.Equal("High", result.ConfidenceLabel);
    }

    [Fact]
    public void Diagnose_DoesNotInventPerformanceIssue_WhenEvidenceIsNormal()
    {
        var input = CreateInput(
            ramUsage: 46,
            browserHeavy: false,
            browserMemoryMb: 900,
            diskUsage: 45,
            mode: "scenario",
            scenarioId: "slow-system");

        var result = _service.Diagnose(input);

        Assert.Equal("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Equal("Monitor", result.ActionCategory);
    }

    [Fact]
    public void Diagnose_ReturnsLowStorage_WhenStorageIsCriticallyUsed()
    {
        var input = CreateInput(
            ramUsage: 50,
            browserHeavy: false,
            browserMemoryMb: 900,
            diskUsage: 94,
            mode: "component",
            componentIds: ["storage"]);

        var result = _service.Diagnose(input);

        Assert.Equal("Low Available Storage Space", result.DiagnosedCategory);
        Assert.Equal("Maintain", result.ActionCategory);
        Assert.Equal("High", result.ConfidenceLabel);
    }

    [Fact]
    public void Diagnose_DoesNotUseStorageIssueOutsideStorageFocus()
    {
        var input = CreateInput(
            ramUsage: 50,
            browserHeavy: false,
            browserMemoryMb: 900,
            diskUsage: 95,
            mode: "component",
            componentIds: ["memory"]);

        var result = _service.Diagnose(input);

        Assert.Equal("No Active Issue Detected", result.DiagnosedCategory);
    }

    [Fact]
    public void Diagnose_DoesNotInventNetworkIssue_WhenDnsResolutionSucceeds()
    {
        var input = CreateInput(
            ramUsage: 50,
            browserHeavy: false,
            browserMemoryMb: 900,
            diskUsage: 45,
            mode: "scenario",
            scenarioId: "network-problem",
            dnsResolutionSucceeded: true);

        var result =
            _service.Diagnose(
                input);

        Assert.Equal(
            "No Active Issue Detected",
            result.DiagnosedCategory);

        Assert.Equal(
            "Monitor",
            result.ActionCategory);

        Assert.Contains(
            result.Proof,
            item =>
                item.Label ==
                    "DNS Resolution" &&
                item.Value ==
                    "Succeeded");
    }

    [Fact]
    public void Diagnose_ReturnsNetworkIssue_WhenDnsResolutionFailsWithValidConfiguration()
    {
        var input = CreateInput(
            ramUsage: 50,
            browserHeavy: false,
            browserMemoryMb: 900,
            diskUsage: 45,
            mode: "scenario",
            scenarioId: "network-problem",
            dnsResolutionSucceeded: false);

        var result =
            _service.Diagnose(
                input);

        Assert.Equal(
            "Network issue",
            result.DiagnosedCategory);

        Assert.Equal(
            "Troubleshoot",
            result.ActionCategory);

        Assert.Equal(
            "High",
            result.ConfidenceLabel);

        Assert.Contains(
            result.Proof,
            item =>
                item.Label ==
                    "DNS Resolution" &&
                item.Value ==
                    "Failed");
    }

    private static AutomaticDiagnosisInput CreateInput(
        double ramUsage,
        bool browserHeavy,
        double browserMemoryMb,
        double diskUsage,
        string mode,
        string? scenarioId = null,
        IReadOnlyList<string>? componentIds = null,
        bool dnsResolutionSucceeded = true)
    {
        return new AutomaticDiagnosisInput
        {
            AgentId = "test-agent",
            CommandId = Guid.NewGuid(),
            Mode = mode,
            ComponentIds = componentIds ?? Array.Empty<string>(),
            ScenarioId = scenarioId,
            CapturedAt = DateTimeOffset.UtcNow,
            Hardware = new HardwareProfileDto
            {
                DeviceName = "TEST-PC",
                OsVersion = "Windows 11",
                SystemAge = "~1 year",
                ChipsetDriver = "Test",
                PrimaryStorageType = "NVMe SSD",

                Cpu = new()
                {
                    Name = "Test CPU",
                    UsagePercent = 0,
                    Cores = 8,
                    Threads = 16,
                    FrequencyMhz = 0
                },

                Gpu = new()
                {
                    Name = "Test GPU",
                    Driver = "1.0",
                    Type = "Integrated",
                    VramGb = 1
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
                    AdapterName = "Test Ethernet",
                    HasIpv4Address = true,
                    HasDefaultGateway = true,
                    HasDnsServers = true,
                    DnsResolutionSucceeded = dnsResolutionSucceeded,
                    DnsTestHost = "example.com",
                    DnsResolutionMessage = dnsResolutionSucceeded
                        ? "DNS resolution for example.com succeeded."
                        : "DNS resolution failed during the test."
                },

                StorageDrives =
                [
                    new()
                    {
                        Model = "Test NVMe",
                        Type = "NVMe SSD",
                        SizeGb = 512,
                        Interface = "NVMe"
                    }
                ],

                AllDisks =
                [
                    new()
                    {
                        Drive = "C:\\",
                        Mountpoint = "C:\\",
                        FsType = "NTFS",
                        TotalGb = 512,
                        UsedGb = 512 * (diskUsage / 100),
                        UsagePercent = diskUsage
                    }
                ],

                ProcessInsights = new()
                {
                    BrowserDetected = true,
                    BrowserProcessCount = browserHeavy ? 20 : 3,
                    BrowserMemoryMb = browserMemoryMb,
                    BrowserHeavy = browserHeavy,
                    GameDetected = false,
                    GameProcesses = [],
                    TopMemoryApps = []
                }
            }
        };
    }
}