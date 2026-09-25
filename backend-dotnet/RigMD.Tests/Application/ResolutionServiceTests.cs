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

    [Fact]
    public void CheckResolution_ResolvesPhase1AndCanonicalCategories_WhenTelemetryReturnsToNormal()
    {
        var hardware = CreateHardware(
            ramUsage: 48,
            browserHeavy: false,
            browserMemoryMb: 500,
            cpuUsage: 18,
            storageUsage: 52,
            dnsResolutionSucceeded: true);

        Assert.Equal("resolved", _service.CheckResolution("Critical Memory Pressure", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Thermal Throttling Detected", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Storage Capacity Warning", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Display or Driver Instability", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Boot or Startup Contention", hardware).resolution_status);
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_HonorsComponentFilterAndGeneratesProof()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 92, // Critical RAM pressure, but we will filter ONLY to 'storage' (which is healthy at 45%)
            browserHeavy: true,
            browserMemoryMb: 4200,
            cpuUsage: 14,
            storageUsage: 45,
            dnsResolutionSucceeded: true);

        var storageOnlyResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component",
            ComponentIds = ["storage"],
            Hardware = hardware
        });

        // Since only storage was selected and storage is healthy at 45%, Primary Verdict is healthy (No Active Issue Detected)
        // while the 92% unselected RAM spike is surfaced strictly as an IncidentalWarning.
        Assert.Equal("No Active Issue Detected", storageOnlyResult.DiagnosedCategory);
        Assert.Equal(ComponentStatus.Present, storageOnlyResult.ComponentStatus);
        Assert.Contains("Storage / SSD / HDD is operating normally", storageOnlyResult.PrimaryResult);
        Assert.NotNull(storageOnlyResult.IncidentalWarning);
        Assert.Contains("Although you only scanned Storage / SSD / HDD", storageOnlyResult.IncidentalWarning);
        Assert.Contains("Memory (RAM)", storageOnlyResult.IncidentalWarning);
        Assert.NotEmpty(storageOnlyResult.Proof);
        Assert.Contains(storageOnlyResult.Proof, p => p.Label.Contains("Storage"));
        Assert.DoesNotContain(storageOnlyResult.Proof, p => p.Label.Contains("Memory (RAM)"));

        // Now run with 'memory' included -> should detect elevated memory pressure with proof
        var memoryResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component",
            ComponentIds = ["memory"],
            Hardware = hardware
        });

        Assert.Equal("High Memory Pressure", memoryResult.DiagnosedCategory);
        Assert.NotEmpty(memoryResult.Proof);
        Assert.Contains(memoryResult.Proof, p => p.Label == "Physical Memory (RAM)" && p.Status == "high");
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_ReturnsNotPresent_WhenDesktopHasNoBattery()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 42,
            browserHeavy: false,
            browserMemoryMb: 600);
        hardware.DeviceType = "Desktop";
        hardware.Battery = null;

        var batteryResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component",
            ComponentIds = ["battery"],
            Hardware = hardware
        });

        Assert.Equal(ComponentStatus.NotPresent, batteryResult.ComponentStatus);
        Assert.Equal("No battery detected on this device", batteryResult.PrimaryResult);
        Assert.Contains(batteryResult.Proof, p => p.Value == "Not Present (Desktop PC)" && p.Meaning.Contains("No battery detected on this device"));
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_SeparatesScopedMemoryPrimaryResultFromCriticalStorageIncidentalWarning()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 42,
            browserHeavy: false,
            browserMemoryMb: 500,
            cpuUsage: 12,
            storageUsage: 95,
            dnsResolutionSucceeded: true);

        var result = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component",
            ComponentIds = ["memory"],
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", result.DiagnosedCategory);
        Assert.Contains("Memory (RAM)", result.TargetScope);
        Assert.Contains("Memory (RAM) is operating normally", result.PrimaryResult);
        Assert.Contains("42% in use, no leaks detected", result.PrimaryResult);
        Assert.NotNull(result.IncidentalWarning);
        Assert.Contains("Note: Although you only scanned Memory (RAM)", result.IncidentalWarning);
        Assert.Contains("critically low on space", result.IncidentalWarning);
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
