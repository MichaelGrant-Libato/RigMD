using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Application.Services;

public class ResolutionResultDto
{
    public string resolution_status { get; set; } = "needs_recheck";
    public string resolution_checked_at { get; set; } = DateTime.UtcNow.ToString("o");
    public string resolution_summary { get; set; } = string.Empty;
    public object[] resolution_proof { get; set; } = Array.Empty<object>();
}

public class ResolutionService
{
    private const double ElevatedCpuThreshold = 90;
    private const double SevereResourceThreshold = 85;
    private const double ElevatedMemoryThreshold = 80;
    private const double BrowserMemoryPressureMb = 3000;
    private const double ElevatedStorageThreshold = 80;
    private const double ElevatedTempThresholdCelsius = 85;

    public ResolutionResultDto CheckResolution(
        string diagnosedCategory,
        HardwareProfileDto hardware,
        string? scenarioId = null,
        string? componentIds = null)
    {
        var normalizedScenario = DiagnosticScopeMapper.NormalizeScenarioId(scenarioId);
        if (!string.IsNullOrWhiteSpace(normalizedScenario))
        {
            switch (normalizedScenario)
            {
                case "overheating-loud-fan":
                    return CheckThermalResolution(hardware);
                case "driver-error":
                case "no-display":
                    return CheckDriverAndDisplayResolution(hardware);
                case "network-issue":
                    return CheckNetworkResolution(hardware);
                case "rapid-battery-drain":
                    return CheckBatteryResolution(hardware);
                case "disk-full":
                    return CheckStorageUsageResolution(
                        hardware,
                        storageMustBeBelow: ElevatedStorageThreshold,
                        issueName: "high storage use");
                case "app-crashes":
                case "blue-screen-crash":
                    return CheckCrashStabilityResolution(hardware);
                case "slow-boot":
                    return CheckBootStartupResolution(hardware);
                case "slow-system":
                case "stuttering-freezing":
                    return CheckOsPerformanceResolution(hardware);
            }
        }

        var parsedComponents = ParseComponentIds(componentIds);
        if (parsedComponents.Count > 0)
        {
            if (parsedComponents.Contains("network") && parsedComponents.Count == 1)
            {
                return CheckNetworkResolution(hardware);
            }

            if (parsedComponents.Contains("battery") && parsedComponents.Count == 1)
            {
                return CheckBatteryResolution(hardware);
            }

            if (parsedComponents.Contains("storage") && parsedComponents.Count == 1)
            {
                return hardware.StorageDrives.Any(d => d.IsFailingSmart)
                    ? CheckStorageHealthResolution(hardware)
                    : CheckStorageUsageResolution(
                        hardware,
                        storageMustBeBelow: ElevatedStorageThreshold,
                        issueName: "high storage use");
            }

            if (parsedComponents.All(c => c is "drivers" or "gpu" or "display"))
            {
                return CheckDriverAndDisplayResolution(hardware);
            }

            if (parsedComponents.Contains("cpu") && parsedComponents.Count == 1)
            {
                return CheckThermalResolution(hardware);
            }

            if (parsedComponents.Contains("memory") && parsedComponents.Count == 1)
            {
                return CheckMemoryResolution(
                    hardware,
                    memoryMustBeBelow: ElevatedMemoryThreshold,
                    issueName: "elevated memory use");
            }
        }

        var category =
            (diagnosedCategory ?? string.Empty).ToLowerInvariant();

        if (category.Contains("severe system resource exhaustion"))
        {
            return CheckSevereResourceResolution(hardware);
        }

        if (category.Contains("thermal"))
        {
            return CheckThermalResolution(hardware);
        }

        if (category.Contains("elevated cpu") || category.Contains("cpu & thermal") || category.Contains("cpu load"))
        {
            return CheckCpuResolution(hardware);
        }

        if (category.Contains("application crash") || category.Contains("system crash") || category.Contains("stop error"))
        {
            return CheckCrashStabilityResolution(hardware);
        }

        if (category.Contains("boot and startup") || category.Contains("boot or startup"))
        {
            return CheckBootStartupResolution(hardware);
        }

        if (category.Contains("os performance"))
        {
            return CheckOsPerformanceResolution(hardware);
        }

        if (category.Contains("high memory pressure") || category.Contains("critical memory pressure") || category.Contains("memory resource pressure") || category.Contains("memory pressure / high ram"))
        {
            return CheckMemoryResolution(
                hardware,
                memoryMustBeBelow: ElevatedMemoryThreshold,
                issueName: "very high memory use");
        }

        if (category.Contains("elevated memory pressure"))
        {
            return CheckWorkloadMemoryResolution(hardware);
        }

        if (category.Contains("elevated memory usage") || category.Contains("memory"))
        {
            return CheckMemoryResolution(
                hardware,
                memoryMustBeBelow: ElevatedMemoryThreshold,
                issueName: "elevated memory use");
        }

        if (category.Contains("storage health"))
        {
            return CheckStorageHealthResolution(hardware);
        }

        if (category.Contains("low available storage") || category.Contains("storage capacity"))
        {
            return CheckStorageUsageResolution(
                hardware,
                storageMustBeBelow: ElevatedStorageThreshold,
                issueName: "low storage space");
        }

        if (category.Contains("elevated storage") || category.Contains("storage"))
        {
            return CheckStorageUsageResolution(
                hardware,
                storageMustBeBelow: ElevatedStorageThreshold,
                issueName: "high storage use");
        }

        if (category.Contains("network"))
        {
            return CheckNetworkResolution(hardware);
        }

        if (category.Contains("battery") || category.Contains("power"))
        {
            return CheckBatteryResolution(hardware);
        }

        if (category.Contains("driver") || category.Contains("display"))
        {
            return CheckDriverAndDisplayResolution(hardware);
        }

        return CheckSevereResourceResolution(hardware);
    }

    private static HashSet<string> ParseComponentIds(string? rawComponents)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawComponents))
        {
            return set;
        }

        var trimmed = rawComponents.Trim();
        if (trimmed.StartsWith("["))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null)
                {
                    foreach (var item in parsed)
                    {
                        var norm = DiagnosticScopeMapper.NormalizeComponentId(item);
                        if (!string.IsNullOrWhiteSpace(norm))
                        {
                            set.Add(norm);
                        }
                    }
                }
                return set;
            }
            catch
            {
                // Fall back to comma split
            }
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var norm = DiagnosticScopeMapper.NormalizeComponentId(part);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                set.Add(norm);
            }
        }

        return set;
    }

    private static ResolutionResultDto CheckDriverAndDisplayResolution(
        HardwareProfileDto hardware)
    {
        var errCount = hardware.DeviceErrors?.Count ?? 0;
        var gpuTemp = hardware.Gpu.TemperatureCelsius;
        var gpuTempOk = !gpuTemp.HasValue || gpuTemp.Value < ElevatedTempThresholdCelsius;
        var driversOk = errCount == 0;
        var resolved = driversOk && gpuTempOk;

        var gpuSummaryValue = gpuTemp.HasValue
            ? $"{hardware.Gpu.Name} ({gpuTemp.Value:0.#}°C, Driver {hardware.Gpu.Driver})"
            : $"{hardware.Gpu.Name} (Driver {hardware.Gpu.Driver})";

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows zero PnP device error codes and normal graphics adapter telemetry."
                : $"Fresh scan still shows {errCount} device error code(s) or elevated graphics adapter temperature.",
            new object[]
            {
                Proof(
                    "Device Manager Errors",
                    errCount == 0 ? "0 active errors" : $"{errCount} active error(s)",
                    driversOk,
                    "All hardware drivers are reporting healthy status (ErrorCode = 0).",
                    "One or more devices still report a non-zero ConfigManagerErrorCode."),
                Proof(
                    "Graphics adapter",
                    gpuSummaryValue,
                    gpuTempOk,
                    $"Graphics adapter and {Math.Max(hardware.ConnectedDisplays, 1)} connected display(s) are operating normally.",
                    "Graphics adapter temperature is still elevated.")
            });
    }

    private static ResolutionResultDto CheckBatteryResolution(
        HardwareProfileDto hardware)
    {
        var battery = hardware.Battery;
        if (battery == null || !battery.HasBattery)
        {
            return CreateResult(
                true,
                "This desktop device runs on direct AC power and has no battery warning.",
                new object[]
                {
                    Proof(
                        "Power / Battery",
                        $"AC Power ({hardware.ActivePowerPlan})",
                        true,
                        "Device is operating normally on AC power.",
                        "Battery or power status needs review.")
                });
        }

        var cpuOk = hardware.Cpu.UsagePercent < ElevatedCpuThreshold;
        var chargeOk = battery.ChargePercent > 15 || battery.IsCharging;
        var resolved = cpuOk && chargeOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows battery charge and background processor load within normal limits."
                : "Fresh scan still shows low battery charge or high background processor drain.",
            new object[]
            {
                Proof(
                    "Battery status",
                    $"{battery.ChargePercent}% ({battery.StatusDescription})",
                    chargeOk,
                    "Battery charge level and power state are healthy.",
                    "Battery charge is low while discharging."),
                Proof(
                    "Background processor load",
                    $"{hardware.Cpu.UsagePercent:0.##}% ({hardware.ActivePowerPlan})",
                    cpuOk,
                    "Processor activity is low enough to prevent rapid battery drain.",
                    "High background processor load is still contributing to battery drain.")
            });
    }

    private static ResolutionResultDto CheckCrashStabilityResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;
        var errCount = hardware.DeviceErrors?.Count ?? 0;

        var cpuOk = cpuUsage < ElevatedCpuThreshold;
        var ramOk = ramUsage < ElevatedMemoryThreshold;
        var driversOk = errCount == 0;
        var resolved = cpuOk && ramOk && driversOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows stable processor, memory, and device driver status. No active issue is detected for this saved check."
                : "Fresh scan still shows resource pressure or active device error codes that may contribute to instability.",
            new object[]
            {
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor activity is within normal operating limits.",
                    "Processor activity is still elevated."),
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is within normal operating limits.",
                    "Memory use is still above RigMD's high-use threshold."),
                Proof(
                    "Device Manager Errors",
                    errCount == 0 ? "0 active errors" : $"{errCount} active error(s)",
                    driversOk,
                    "All hardware drivers report healthy status (ErrorCode = 0).",
                    "One or more devices still report an active error code.")
            });
    }

    private static ResolutionResultDto CheckBootStartupResolution(
        HardwareProfileDto hardware)
    {
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;

        var storageOk = primaryDisk == null || storageUsage < ElevatedStorageThreshold;
        var cpuOk = cpuUsage < ElevatedCpuThreshold;
        var ramOk = ramUsage < ElevatedMemoryThreshold;
        var resolved = storageOk && cpuOk && ramOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows boot storage, processor, and memory operating normally. No active issue is detected for this saved check."
                : "Fresh scan still shows elevated boot storage or startup resource usage.",
            new object[]
            {
                Proof(
                    "Main storage use",
                    primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                    storageOk,
                    "Boot drive free space is within a healthy range for Windows startup.",
                    "Boot drive space is still tight and may slow down startup caching."),
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor startup load is within normal limits.",
                    "Processor load is still elevated."),
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is within normal limits.",
                    "Memory use is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckSevereResourceResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;

        var cpuOk = cpuUsage < SevereResourceThreshold;
        var ramOk = ramUsage < SevereResourceThreshold;
        var storageOk = storageUsage < SevereResourceThreshold;
        var resolved = cpuOk && ramOk && storageOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows the combined CPU, memory, and storage pressure. No active issue is detected for this saved check."
                : "Fresh scan still shows heavy pressure on at least one major part of the device.",
            new object[]
            {
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    cpuOk,
                    "Processor activity is back under RigMD's severe-pressure range.",
                    "Processor activity is still inside RigMD's severe-pressure range."),
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is back under RigMD's severe-pressure range.",
                    "Memory use is still inside RigMD's severe-pressure range."),
                Proof(
                    "Storage use",
                    primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                    storageOk,
                    "Storage use is back under RigMD's severe-pressure range.",
                    "Storage use is still inside RigMD's severe-pressure range.")
            });
    }

    private static ResolutionResultDto CheckThermalResolution(
        HardwareProfileDto hardware)
    {
        var tempC = hardware.Cpu.TemperatureCelsius;
        var tempOk = !tempC.HasValue || tempC.Value < ElevatedTempThresholdCelsius;
        var throttleOk = !hardware.Cpu.IsThermallyThrottling;
        var resolved = tempOk && throttleOk;

        var tempDisplay = tempC.HasValue
            ? $"{tempC.Value:0.#}°C ({hardware.Cpu.UsagePercent:0.#}% load)"
            : $"Nominal ({hardware.Cpu.UsagePercent:0.#}% load)";

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows processor temperature and throttling within normal limits. No active issue is detected for this saved check."
                : "Fresh scan still shows elevated processor temperature or thermal throttling behavior.",
            new object[]
            {
                Proof(
                    "Processor temperature",
                    tempDisplay,
                    tempOk,
                    "Processor temperature is within safe operating limits.",
                    "Processor temperature is still elevated (>= 85°C)."),
                Proof(
                    "Processor throttling",
                    hardware.Cpu.IsThermallyThrottling ? "Still observed" : "Not observed",
                    throttleOk,
                    "RigMD no longer sees the processor slowing itself down because of heat.",
                    "RigMD still sees signs that the processor may be slowing itself down because of heat.")
            });
    }

    private static ResolutionResultDto CheckCpuResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var resolved = cpuUsage < ElevatedCpuThreshold;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows very high processor activity. No active issue is detected for this saved check."
                : "Fresh scan still shows very high processor activity.",
            new object[]
            {
                Proof(
                    "Processor activity",
                    $"{cpuUsage:0.##}%",
                    resolved,
                    "Processor activity is back below RigMD's high-use range.",
                    "Processor activity is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckOsPerformanceResolution(
        HardwareProfileDto hardware)
    {
        var cpuUsage = hardware.Cpu.UsagePercent;
        var ramUsage = hardware.Ram.UsagePercent;
        var primaryDisk = GetPrimaryDisk(hardware);
        var storageUsage = primaryDisk?.UsagePercent ?? 0;

        var memoryWarning = hardware.ProcessInsights.MemoryLeakWarning;
        var browserMemoryMb = hardware.ProcessInsights.BrowserMemoryMb;
        var browserProcessCount = hardware.ProcessInsights.BrowserProcessCount;

        var cpuOk = cpuUsage < ElevatedCpuThreshold;
        var ramOk = ramUsage < ElevatedMemoryThreshold;
        var storageOk = primaryDisk == null || storageUsage < ElevatedStorageThreshold;
        var browserOk = browserMemoryMb < BrowserMemoryPressureMb && browserProcessCount < 25;
        var warningOk = string.IsNullOrWhiteSpace(memoryWarning);

        var resolved = cpuOk && ramOk && storageOk && browserOk && warningOk;

        var proofItems = new List<object>
        {
            Proof(
                "Processor activity",
                $"{cpuUsage:0.##}%",
                cpuOk,
                "Processor activity is within normal operating limits.",
                "Processor activity is still elevated and may slow down system responsiveness."),
            Proof(
                "Memory use",
                $"{ramUsage:0.##}%",
                ramOk,
                "Memory use is back below RigMD's high-use range.",
                "Memory use is still above RigMD's high-use range."),
            Proof(
                "Main storage use",
                primaryDisk == null ? "Not available" : $"{storageUsage:0.##}%",
                storageOk,
                "Main storage free space is within a healthy range.",
                "Main storage space is still tight and may affect Windows responsiveness.")
        };

        if (!browserOk || browserMemoryMb >= 1500)
        {
            proofItems.Add(
                Proof(
                    "Browser workload",
                    $"{browserMemoryMb:0.##} MB across {browserProcessCount} processes",
                    browserOk,
                    "Browser workload is within normal limits.",
                    "Browser workload is still heavy enough to affect performance."));
        }

        if (!warningOk)
        {
            proofItems.Add(
                Proof(
                    "Unusual app memory behavior",
                    memoryWarning!,
                    false,
                    "RigMD no longer sees one app using unusually high memory.",
                    "RigMD still sees app memory behavior that needs attention."));
        }

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows processor, memory, and storage operating within normal limits. No active issue is detected for this saved check."
                : "Fresh scan still shows system resource pressure or app memory behavior that may affect performance.",
            proofItems.ToArray());
    }

    private static ResolutionResultDto CheckMemoryResolution(
        HardwareProfileDto hardware,
        double memoryMustBeBelow,
        string issueName)
    {
        var ramUsage = hardware.Ram.UsagePercent;
        var resolved = ramUsage < memoryMustBeBelow;

        return CreateResult(
            resolved,
            resolved
                ? $"Fresh scan no longer shows {issueName}. No active issue is detected for this saved check."
                : $"Fresh scan still shows {issueName}.",
            new object[]
            {
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    resolved,
                    "Memory use is back below RigMD's high-use range.",
                    "Memory use is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckWorkloadMemoryResolution(
        HardwareProfileDto hardware)
    {
        var ramUsage = hardware.Ram.UsagePercent;
        var processes = hardware.ProcessInsights;
        var browserMemoryMb = processes.BrowserMemoryMb;

        var ramOk = ramUsage < ElevatedMemoryThreshold;
        var browserOk =
            !processes.BrowserHeavy ||
            browserMemoryMb < BrowserMemoryPressureMb;

        var resolved = ramOk && browserOk;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows high memory pressure from active apps. No active issue is detected for this saved check."
                : "Fresh scan still shows memory pressure from active apps.",
            new object[]
            {
                Proof(
                    "Memory use",
                    $"{ramUsage:0.##}%",
                    ramOk,
                    "Memory use is back below RigMD's high-use range.",
                    "Memory use is still above RigMD's high-use range."),
                Proof(
                    "Browser workload",
                    $"{browserMemoryMb:0.##} MB",
                    browserOk,
                    "Browser activity is no longer heavy enough to explain the saved memory-pressure problem.",
                    "Browser activity is still high enough to contribute to memory pressure.")
            });
    }

    private static ResolutionResultDto CheckStorageHealthResolution(
        HardwareProfileDto hardware)
    {
        var failingDrives = hardware.StorageDrives.Count(drive => drive.IsFailingSmart);
        var resolved = failingDrives == 0;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan no longer shows a Windows storage-health warning. No active issue is detected for this saved check."
                : "Fresh scan still shows a storage-health warning. Back up important files and get the drive checked.",
            new object[]
            {
                Proof(
                    "Storage health warning",
                    resolved ? "Not observed" : $"{failingDrives} drive(s) affected",
                    resolved,
                    "Windows is no longer reporting a storage-health warning.",
                    "Windows is still reporting a storage-health warning. Back up important files and get the drive checked.")
            });
    }

    private static ResolutionResultDto CheckStorageUsageResolution(
        HardwareProfileDto hardware,
        double storageMustBeBelow,
        string issueName)
    {
        var primaryDisk = GetPrimaryDisk(hardware);
        var usagePercent = primaryDisk?.UsagePercent ?? 0;
        var resolved = primaryDisk != null && usagePercent < storageMustBeBelow;

        return CreateResult(
            resolved,
            resolved
                ? $"Fresh scan no longer shows {issueName}. No active issue is detected for this saved check."
                : primaryDisk == null
                    ? "Fresh scan could not confirm the main storage usage yet."
                    : $"Fresh scan still shows {issueName}.",
            new object[]
            {
                Proof(
                    "Main storage use",
                    primaryDisk == null ? "Not available" : $"{usagePercent:0.##}%",
                    resolved,
                    "Storage use is back below RigMD's high-use range.",
                    primaryDisk == null
                        ? "RigMD could not read the main storage usage yet."
                        : "Storage use is still above RigMD's high-use range.")
            });
    }

    private static ResolutionResultDto CheckNetworkResolution(
        HardwareProfileDto hardware)
    {
        var network = hardware.Network;
        var resolved =
            network.HasActiveAdapter &&
            network.HasIpv4Address &&
            network.HasDefaultGateway &&
            network.HasDnsServers &&
            network.DnsResolutionSucceeded;

        return CreateResult(
            resolved,
            resolved
                ? "Fresh scan shows the internet name check is working again. No active issue is detected for this saved check."
                : "Fresh scan still cannot confirm that the internet name check is working normally.",
            new object[]
            {
                Proof(
                    "Network adapter",
                    network.HasActiveAdapter ? "Found" : "Not found",
                    network.HasActiveAdapter,
                    "Windows can see an active network connection.",
                    "Windows still cannot confirm an active network connection."),
                Proof(
                    "Internet name check",
                    network.DnsResolutionSucceeded ? "Succeeded" : "Failed",
                    network.DnsResolutionSucceeded,
                    "Website names can be turned into reachable addresses again.",
                    "Website names still cannot be turned into reachable addresses.")
            });
    }

    private static DiskVolumeDto? GetPrimaryDisk(HardwareProfileDto hardware)
    {
        return hardware.AllDisks?
            .FirstOrDefault(disk =>
                disk.Drive.Equals("C:\\", StringComparison.OrdinalIgnoreCase) ||
                disk.Mountpoint.Equals("C:\\", StringComparison.OrdinalIgnoreCase))
            ?? hardware.AllDisks?.FirstOrDefault();
    }

    private static ResolutionResultDto CreateResult(
        bool resolved,
        string summary,
        object[] proof)
    {
        return new ResolutionResultDto
        {
            resolution_status = resolved ? "resolved" : "still_active",
            resolution_checked_at = DateTime.UtcNow.ToString("o"),
            resolution_summary = summary,
            resolution_proof = proof
        };
    }

    private static object Proof(
        string label,
        string value,
        bool resolved,
        string normalMeaning,
        string activeMeaning)
    {
        return new
        {
            label,
            value,
            status = resolved ? "normal now" : "still needs attention",
            meaning = resolved ? normalMeaning : activeMeaning
        };
    }
}
