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
            dnsResolutionSucceeded: true,
            cpuTemp: 45);

        Assert.Equal("resolved", _service.CheckResolution("Critical Memory Pressure", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Thermal Throttling Detected", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Storage Capacity Warning", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Display or Driver Instability", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Boot or Startup Contention", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("Application crash history requires review", hardware).resolution_status);
        Assert.Equal("resolved", _service.CheckResolution("System crash or stop error requires review", hardware).resolution_status);
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
        // and out-of-scope RAM pressure does NOT hijack or clutter the scoped component scan.
        Assert.Equal("No Active Issue Detected", storageOnlyResult.DiagnosedCategory);
        Assert.Equal(ComponentStatus.Present, storageOnlyResult.ComponentStatus);
        Assert.Contains("No warning was found", storageOnlyResult.PrimaryResult);
        Assert.Null(storageOnlyResult.IncidentalWarning);
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
        hardware.Battery = new() { HasBattery = false, QuerySucceeded = true };

        var batteryResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component",
            ComponentIds = ["battery"],
            Hardware = hardware
        });

        Assert.Equal(ComponentStatus.NotPresent, batteryResult.ComponentStatus);
        Assert.Equal("No battery detected on this device", batteryResult.PrimaryResult);
        Assert.Contains(batteryResult.Proof, p => p.Value == "No battery reported" && p.Status == "info");
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_StrictlyScopesScenarioScansWithoutMemoryHijacking()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 74, // Above 68% RAM threshold
            browserHeavy: true,
            browserMemoryMb: 2100, // Above 1500 MB browser threshold
            cpuUsage: 22,
            storageUsage: 50,
            dnsResolutionSucceeded: true,
            cpuTemp: 45);

        var overheatingResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "overheating-loud-fan",
            Hardware = hardware
        });

        Assert.Equal("Scanner evidence unavailable", overheatingResult.DiagnosedCategory);
        Assert.DoesNotContain(overheatingResult.Proof, p => p.Status == "high");
        Assert.DoesNotContain("[", overheatingResult.RecommendedNextStep);

        var appCrashResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "app-crashes",
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", appCrashResult.DiagnosedCategory);
        Assert.DoesNotContain("[", appCrashResult.RecommendedNextStep);

        var bsodResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "blue-screen-crash",
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", bsodResult.DiagnosedCategory);
        Assert.DoesNotContain("[", bsodResult.RecommendedNextStep);
        Assert.DoesNotContain(bsodResult.Proof, p => p.Label.Contains("Memory (RAM)"));

        var fullNormalBrowserResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "full",
            Hardware = hardware
        });

        Assert.Equal("Scanner evidence unavailable", fullNormalBrowserResult.DiagnosedCategory);
        Assert.DoesNotContain(fullNormalBrowserResult.Proof, p => p.Status == "high");
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_DoesNotRaiseScenarioIssue_WhenScopedTelemetryIsHealthy()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 62,
            browserHeavy: true,
            browserMemoryMb: 2500,
            cpuUsage: 11,
            cpuTemp: 53.1,
            storageUsage: 73,
            dnsResolutionSucceeded: true,
            pingLatencyMs: 13,
            packetLossPercent: 0);

        var thermalResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "overheating-loud-fan",
            Hardware = hardware
        });

        Assert.Equal("Scanner evidence unavailable", thermalResult.DiagnosedCategory);
        Assert.Contains(thermalResult.Proof, p => p.MetricId == "fan_operation" && p.Status == "unknown");
        Assert.DoesNotContain(thermalResult.Proof, p => p.Label.Contains("Memory"));

        var networkResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "network-problem",
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", networkResult.DiagnosedCategory);
        Assert.Contains("No warning was found", networkResult.PrimaryResult);
        Assert.DoesNotContain(networkResult.Proof, p => p.Label.Contains("Memory"));

        var storageResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "storage-problem",
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", storageResult.DiagnosedCategory);
        Assert.Contains("No warning was found", storageResult.PrimaryResult);
        Assert.DoesNotContain(storageResult.Proof, p => p.Label.Contains("Memory"));
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_ThermalScenarioDoesNotSurfaceDriverErrorsAsThermalProof()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 64,
            browserHeavy: false,
            browserMemoryMb: 900,
            cpuUsage: 22,
            cpuTemp: 53.1,
            storageUsage: 73,
            deviceErrorCount: 1);

        var thermalResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "overheating-loud-fan",
            Hardware = hardware
        });

        Assert.Equal("Scanner evidence unavailable", thermalResult.DiagnosedCategory);
        Assert.Contains(thermalResult.Proof, p => p.Label == "Graphics Thermal Context");
        Assert.DoesNotContain(thermalResult.Proof, p => p.Value.Contains("PnP Device Error"));

        var driverResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "driver-error",
            Hardware = hardware
        });

        Assert.Contains(driverResult.Proof, p => p.MetricId.StartsWith("device_error_") && p.Status == "high");
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_DriverScenarioDoesNotReportConflict_WhenDeviceManagerIsClean()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 64,
            browserHeavy: false,
            browserMemoryMb: 900,
            cpuUsage: 18,
            deviceErrorCount: 0);

        var driverResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "driver-error",
            Hardware = hardware
        });

        Assert.Equal("No Active Issue Detected", driverResult.DiagnosedCategory);
        Assert.Equal("Monitor", driverResult.ActionCategory);
        Assert.Contains("No warning was found", driverResult.PrimaryResult);
        Assert.Contains(driverResult.Proof, p => p.Label == "Graphics & Drivers" && p.Value.StartsWith("0 device error") && p.Status == "info");
    }

    [Fact]
    public void AutomaticDiagnosisService_Diagnose_AppCrashScenarioSeparatesCrashEvidenceFromMemoryRisk()
    {
        var autoService = new AutomaticDiagnosisService();
        var hardware = CreateHardware(
            ramUsage: 84,
            browserHeavy: true,
            browserMemoryMb: 3400,
            cpuUsage: 19,
            appCrashEventsAfter: 0);

        var appCrashResult = autoService.Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "scenario",
            ScenarioId = "app-crashes",
            Hardware = hardware
        });

        Assert.Equal("Application stability risk needs review", appCrashResult.DiagnosedCategory);
        Assert.DoesNotContain(appCrashResult.Proof, p => p.MetricId == "application_crash_history" && p.Status == "high");
        Assert.Contains(appCrashResult.Proof, p => p.MetricId == "memory_usage" && p.Status == "high");
        Assert.Contains(appCrashResult.Proof, p => p.Label.StartsWith("Application crash") && p.Status == "normal");
    }

    [Fact]
    public void CheckResolution_PrioritizesScenarioAndComponentScopeOverSavedCategory()
    {
        var hardware = CreateHardware(
            ramUsage: 88,
            browserHeavy: true,
            browserMemoryMb: 3800,
            cpuUsage: 20,
            storageUsage: 45,
            dnsResolutionSucceeded: true,
            cpuTemp: 45);

        // Even if an older saved session had 'Elevated Memory Pressure From Active Workloads',
        // passing scenarioId='overheating-loud-fan' or 'driver-error' checks thermal or driver status instead of RAM/Browser.
        var thermalCheck = _service.CheckResolution(
            "Elevated Memory Pressure From Active Workloads",
            hardware,
            scenarioId: "overheating-loud-fan");
        Assert.Equal("needs_recheck", thermalCheck.resolution_status);
        Assert.Contains(thermalCheck.resolution_proof, p => p.ToString()!.Contains("cpu_temperature"));

        var driverCheck = _service.CheckResolution(
            "Elevated Memory Pressure From Active Workloads",
            hardware,
            scenarioId: "driver-error");
        Assert.Equal("resolved", driverCheck.resolution_status);
        Assert.Contains(driverCheck.resolution_proof, p => p.ToString()!.Contains("graphics_devices"));

        var networkComponentCheck = _service.CheckResolution(
            "Elevated Memory Pressure From Active Workloads",
            hardware,
            scenarioId: null,
            componentIds: "[\"network\"]");
        Assert.Equal("resolved", networkComponentCheck.resolution_status);
        Assert.Contains(networkComponentCheck.resolution_proof, p => p.ToString()!.Contains("network_dns"));
    }

    [Fact]
    public void CheckResolution_UsesScenarioSpecificProofInsteadOfGenericResourceProof()
    {
        var hardware = CreateHardware(
            ramUsage: 84,
            browserHeavy: true,
            browserMemoryMb: 3600,
            cpuUsage: 22,
            cpuTemp: 53.1,
            storageUsage: 73,
            dnsResolutionSucceeded: true,
            pingLatencyMs: 12,
            packetLossPercent: 0,
            deviceErrorCount: 1);

        var thermalCheck = _service.CheckResolution(
            "No Active Issue Detected",
            hardware,
            scenarioId: "overheating-loud-fan");
        var thermalProof = Assert.IsAssignableFrom<object[]>(thermalCheck.resolution_proof);
        Assert.Contains(thermalProof, p => p.ToString()!.Contains("Processor temperature"));
        Assert.DoesNotContain(thermalProof, p => p.ToString()!.Contains("Device Manager"));
        Assert.DoesNotContain(thermalProof, p => p.ToString()!.Contains("Memory use"));

        var networkCheck = _service.CheckResolution(
            "No Active Issue Detected",
            hardware,
            scenarioId: "network-problem");
        var networkProof = Assert.IsAssignableFrom<object[]>(networkCheck.resolution_proof);
        Assert.Contains(networkProof, p => p.ToString()!.Contains("network_latency"));
        Assert.Contains(networkProof, p => p.ToString()!.Contains("network_loss"));
        Assert.DoesNotContain(networkProof, p => p.ToString()!.Contains("Memory use"));

        var storageCheck = _service.CheckResolution(
            "No Active Issue Detected",
            hardware,
            scenarioId: "storage-problem");
        var storageProof = Assert.IsAssignableFrom<object[]>(storageCheck.resolution_proof);
        Assert.Contains(storageProof, p => p.ToString()!.Contains("storage_usage"));
        Assert.Contains(storageProof, p => p.ToString()!.Contains("storage_health"));
        Assert.DoesNotContain(storageProof, p => p.ToString()!.Contains("Device Manager"));
    }

    [Fact]
    public void CheckResolution_AppCrashUsesEventLogEvidenceSinceDiagnosis()
    {
        var diagnosisTime = DateTimeOffset.UtcNow.AddMinutes(-30);
        var hardware = CreateHardware(
            ramUsage: 45,
            browserHeavy: false,
            browserMemoryMb: 500,
            cpuUsage: 8,
            appCrashEventsAfter: 1,
            eventTime: DateTimeOffset.UtcNow.AddMinutes(-5));

        var result = _service.CheckResolution(
            "Application crash history requires review",
            hardware,
            scenarioId: "app-crashes",
            sessionCreatedAt: diagnosisTime);

        var proof = Assert.IsAssignableFrom<object[]>(result.resolution_proof);
        Assert.Equal("still_active", result.resolution_status);
        Assert.Contains(proof, p => p.ToString()!.Contains("Application crash records since diagnosis") && p.ToString()!.Contains("1 found"));
        Assert.DoesNotContain(proof, p => p.ToString()!.Contains("Main storage"));
    }

    [Fact]
    public void CheckResolution_AppCrashDoesNotResolveWhenEventLogCannotBeVerified()
    {
        var hardware = CreateHardware(
            ramUsage: 42,
            browserHeavy: false,
            browserMemoryMb: 500,
            cpuUsage: 7,
            eventQueryWarning: "Windows Event Log query timed out for Application.");

        var result = _service.CheckResolution(
            "Application crash history requires review",
            hardware,
            scenarioId: "app-crashes",
            sessionCreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30));

        Assert.Equal("needs_recheck", result.resolution_status);
        Assert.Contains("Could not verify", result.resolution_summary);
        Assert.Contains(result.resolution_proof, p => p.ToString()!.Contains("unknown"));
    }

    [Fact]
    public void CheckResolution_AppCrashUsesMemoryAndDriverOnlyAsSupportingSignals()
    {
        var hardware = CreateHardware(
            ramUsage: 84,
            browserHeavy: false,
            browserMemoryMb: 700,
            cpuUsage: 11,
            deviceErrorCount: 1);

        var result = _service.CheckResolution(
            "Application crash history requires review",
            hardware,
            scenarioId: "app-crashes",
            sessionCreatedAt: DateTimeOffset.UtcNow.AddMinutes(-30));

        var proof = Assert.IsAssignableFrom<object[]>(result.resolution_proof);
        Assert.Equal("still_active", result.resolution_status);
        Assert.Contains(proof, p => p.ToString()!.Contains("Application crash records since diagnosis") && p.ToString()!.Contains("0 found"));
        Assert.Contains(proof, p => p.ToString()!.Contains("memory_usage") && p.ToString()!.Contains("high"));
        Assert.DoesNotContain(proof, p => p.ToString()!.Contains("device_error_"));
    }

    private static HardwareProfileDto CreateHardware(
        double ramUsage,
        bool browserHeavy,
        double browserMemoryMb,
        double cpuUsage = 15,
        double? cpuTemp = 45,
        double storageUsage = 45,
        bool dnsResolutionSucceeded = true,
        long? pingLatencyMs = 20,
        double? packetLossPercent = 0,
        int deviceErrorCount = 0,
        int appCrashEventsAfter = 0,
        int systemCrashEventsAfter = 0,
        string? eventQueryWarning = null,
        DateTimeOffset? eventTime = null)
    {
        return new HardwareProfileDto
        {
            DeviceErrorsAvailable = true,
            Battery = new() { QuerySucceeded = true, HasBattery = false },
            StorageDrives = [new() { Model = "Test SSD", UsagePercent = storageUsage }],
            Cpu = new()
            {
                UsagePercent = cpuUsage,
                TemperatureCelsius = cpuTemp,
                IsThermallyThrottling = false
            },

            Gpu = new()
            {
                HasGpu = true,
                Name = "Intel Iris Xe Graphics",
                Driver = "31.0.101.4255",
                TemperatureCelsius = 40
            },

            ConnectedDisplays = 1,

            DeviceErrors = Enumerable.Range(0, deviceErrorCount)
                .Select(i => new DeviceErrorDto
                {
                    Name = $"Test Device {i + 1}",
                    DeviceId = $"TEST\\DEVICE\\{i + 1}",
                    ErrorCode = 28,
                    Description = "Test driver warning"
                })
                .ToList(),

            Ram = new()
            {
                TotalGb = 16,
                UsedGb = 16 * (ramUsage / 100),
                UsagePercent = ramUsage
            },

            Network = new()
            {
                QuerySucceeded = true,
                HasActiveAdapter = true,
                HasIpv4Address = true,
                HasDefaultGateway = true,
                HasDnsServers = true,
                DnsResolutionSucceeded = dnsResolutionSucceeded,
                PingLatencyMs = pingLatencyMs,
                PacketLossPercent = packetLossPercent,
                IsWifi = true,
                WifiSignalStrength = 77
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
            },

            StabilityEvents = new()
            {
                QuerySucceeded = string.IsNullOrWhiteSpace(eventQueryWarning),
                QueryWarning = eventQueryWarning,
                ApplicationCrashEvents = Enumerable.Range(0, appCrashEventsAfter)
                    .Select(i => CreateEvent(
                        eventTime ?? DateTimeOffset.UtcNow.AddMinutes(-5 - i),
                        "Application Error",
                        1000,
                        "Faulting application test.exe stopped working."))
                    .ToList(),
                SystemCrashEvents = Enumerable.Range(0, systemCrashEventsAfter)
                    .Select(i => CreateEvent(
                        eventTime ?? DateTimeOffset.UtcNow.AddMinutes(-5 - i),
                        "Microsoft-Windows-Kernel-Power",
                        41,
                        "The system rebooted without cleanly shutting down first."))
                    .ToList()
            }
        };
    }

    private static WindowsEventSummaryDto CreateEvent(
        DateTimeOffset timeUtc,
        string provider,
        int eventId,
        string message)
    {
        return new WindowsEventSummaryDto
        {
            TimeUtc = timeUtc.ToUniversalTime(),
            Level = "Error",
            Provider = provider,
            EventId = eventId,
            Message = message
        };
    }
}
