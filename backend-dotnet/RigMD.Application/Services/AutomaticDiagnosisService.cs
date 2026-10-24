using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Application.Services;

/// <summary>
/// Live-telemetry intake evaluator supporting full, component, and scenario modes.
/// Enforces hardware presence gating (ComponentStatus.NotPresent), strict scoped primary verdicts,
/// and grounded telemetry proof items without cross-mode hijacking.
/// </summary>
public sealed class AutomaticDiagnosisService : IAutomaticDiagnosisService
{
    public AutomaticDiagnosisResult Diagnose(AutomaticDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Hardware);
        return ScannerEvidencePolicy.Complete(input, DiagnoseCore(input));
    }

    private static AutomaticDiagnosisResult DiagnoseCore(AutomaticDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Hardware);

        var hw = input.Hardware;
        var mode = (input.Mode ?? "full").Trim().ToLowerInvariant();
        var scenarioId = DiagnosticScopeMapper.NormalizeScenarioId(input.ScenarioId);
        var selectedComponents = (input.ComponentIds ?? Array.Empty<string>())
            .Select(DiagnosticScopeMapper.NormalizeComponentId)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetScopeLabels = mode == "component" && selectedComponents.Count > 0
            ? selectedComponents.Select(DiagnosticScopeMapper.GetComponentDisplayName).ToList()
            : mode == "scenario" && !string.IsNullOrWhiteSpace(scenarioId)
                ? new List<string> { scenarioId }
                : new List<string> { "Full System" };

        // 0. Hardware Presence Pre-Flight Gate (Prevent False Positives on Absent Parts)
        bool hasBattery = hw.Battery?.HasBattery == true;
        bool hasGpu = hw.Gpu.HasGpu && !string.Equals(hw.Gpu.Name, "Unknown GPU", StringComparison.OrdinalIgnoreCase);

        string? absentComponentNote = null;
        if (mode == "component" && selectedComponents.Count > 0)
        {
            if (selectedComponents.Contains("battery") && !hasBattery)
            {
                if (selectedComponents.Count == 1 && hw.Battery?.QuerySucceeded == true)
                {
                    return new AutomaticDiagnosisResult
                    {
                        ComponentStatus = ComponentStatus.NotPresent,
                        TargetScope = targetScopeLabels,
                        PrimaryResult = "No battery detected on this device",
                        IncidentalWarning = null,
                        DiagnosedCategory = "Component Not Present",
                        ActionCategory = "Monitor",
                        ConfidenceLabel = "High",
                        Explanation = "Windows reported no battery during a completed query. This does not identify the device type or test power delivery.",
                        RecommendedNextStep = "Select present hardware components (such as Processor, Memory, Storage, or Network) to run a targeted check.",
                        Proof = new List<AutomaticDiagnosisProof>
                        {
                            new()
                            {
                                Label = "Battery / Power",
                                Value = "No battery reported",
                                Status = "info",
                                Meaning = "No battery detected on this device via Win32_Battery."
                            }
                        }
                    };
                }

                if (hw.Battery?.QuerySucceeded == true)
                {
                    selectedComponents.Remove("battery");
                    targetScopeLabels = selectedComponents.Select(DiagnosticScopeMapper.GetComponentDisplayName).ToList();
                    absentComponentNote = "Battery / Power was skipped because Windows reported no battery.";
                }
            }

            if (selectedComponents.Contains("gpu") && !hasGpu && selectedComponents.Count == 1)
            {
                return new AutomaticDiagnosisResult
                {
                    ComponentStatus = ComponentStatus.NotPresent,
                    TargetScope = targetScopeLabels,
                    PrimaryResult = "No graphics controller detected on this device",
                    IncidentalWarning = null,
                    DiagnosedCategory = "Component Not Present",
                    ActionCategory = "Monitor",
                    ConfidenceLabel = "High",
                    Explanation = "No graphics controller detected on this device via Win32_VideoController.",
                    RecommendedNextStep = "Verify display adapter status in Windows Device Manager.",
                    Proof = new List<AutomaticDiagnosisProof>
                    {
                        new()
                        {
                            Label = "GPU / Graphics",
                            Value = "Not Present",
                            Status = "normal",
                            Meaning = "No graphics adapter detected via Win32_VideoController."
                        }
                    }
                };
            }
        }

        var cpuUsage = hw.Cpu.UsagePercent;
        var cpuTemp = hw.Cpu.TemperatureCelsius;
        var ramUsage = hw.Ram.UsageAvailable == false ? double.NaN : hw.Ram.UsagePercent;
        var maxDiskUsage = hw.AllDisks.Any()
            ? hw.AllDisks.Max(d => d.UsagePercent)
            : hw.StorageDrives.FirstOrDefault()?.UsagePercent ?? 0;
        var smartFailing = hw.StorageDrives.Any(d => d.IsFailingSmart);
        var deviceErrorsInScope = DeviceErrorAttribution.SelectForScope(hw.DeviceErrors, mode, scenarioId, selectedComponents);
        var hasDeviceErrors = deviceErrorsInScope.Count > 0;
        var browserMemoryMb = hw.ProcessInsights?.BrowserMemoryMb ?? 0;
        var browserProcs = hw.ProcessInsights?.BrowserProcessCount ?? 0;
        var memLeakWarning = hw.ProcessInsights?.MemoryLeakWarning;

        var proof = BuildTelemetryProof(
            hw,
            mode,
            scenarioId,
            selectedComponents,
            cpuUsage,
            cpuTemp,
            ramUsage,
            maxDiskUsage,
            smartFailing,
            deviceErrorsInScope.Count,
            browserMemoryMb,
            browserProcs,
            input.CapturedAt == default ? null : input.CapturedAt, input.EventsSince);

        // 1. Scenario-driven investigation runs FIRST when user explicitly picked a problem scenario
        // so general RAM/browser workload never hijacks a targeted Scenario Scan.
        if (mode == "scenario" && !string.IsNullOrWhiteSpace(scenarioId))
        {
            return BuildScenarioDiagnosis(
                scenarioId,
                targetScopeLabels,
                hw,
                cpuUsage,
                cpuTemp,
                ramUsage,
                maxDiskUsage,
                smartFailing,
                hasDeviceErrors,
                proof);
        }

        bool EvalComponent(params string[] ids) =>
            mode == "full" || (mode == "component" && selectedComponents.Count > 0 && ids.Any(selectedComponents.Contains));

        // 2. Hardware health critical checks (SMART failure / PnP device error codes)
        if (EvalComponent("storage") && smartFailing)
        {
            var primary = "Live S.M.A.R.T. telemetry indicates a storage drive health warning. Back up important files immediately before running disk repairs.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Storage health behavior",
                ActionCategory = "Escalate",
                ConfidenceLabel = "High",
                Explanation = primary,
                RecommendedNextStep = "Back up important files first, then run the guided inspection below to review drive health and Windows Event Logs.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "storage_settings",
                    Label = "Windows Storage Settings",
                    Description = "Inspect physical drive health and volume status."
                }
            };
        }

        if (EvalComponent("drivers", "gpu", "display", "peripherals", "audio") && hasDeviceErrors)
        {
            var firstErr = deviceErrorsInScope.First();
            var primary = $"Windows Device Manager reported error code {firstErr.ErrorCode} on '{firstErr.Name}'.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Driver conflict",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = primary,
                RecommendedNextStep = "Open Windows Device Manager or run the guided inspection below to check driver status and system error logs.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "device_manager",
                    Label = "Windows Device Manager",
                    Description = "Inspect devices reporting non-zero error codes."
                }
            };
        }

        // 3. Combined severe system thrashing (only when CPU, Memory, and Storage are all in scope)
        if (EvalComponent("cpu") && EvalComponent("memory") && EvalComponent("storage") &&
            cpuUsage >= 85 && ramUsage >= 85)
        {
            var primary = $"High processor activity ({cpuUsage:0.#}%) and memory use ({ramUsage:0.#}%) were measured together. Storage capacity use ({maxDiskUsage:0.#}%) is a separate reading, not disk activity.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Severe System Resource Exhaustion",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = primary,
                RecommendedNextStep = "Run the guided inspection below to close heavy background apps or clear temporary disk caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect combined CPU, memory, and disk resource consumers."
                }
            };
        }

        // 4. CPU & Thermal checks
        if ((EvalComponent("cpu", "thermal") && (hw.Cpu.IsThermallyThrottling || cpuTemp is >= 85)) ||
            (EvalComponent("thermal", "gpu") && hw.Gpu.TemperatureCelsius is >= 85))
        {
            var primary = hw.Cpu.IsThermallyThrottling || cpuTemp is >= 85
                ? $"Thermal pressure detected on {hw.Cpu.Name} ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermal throttling reported; CPU temperature unavailable")})."
                : $"Graphics temperature is elevated on {hw.Gpu.Name} ({hw.Gpu.TemperatureCelsius:0.#}°C). CPU temperature is {(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "unavailable")}.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = primary,
                RecommendedNextStep = "Check airflow and cooling, or run the guided inspection below to review processor temperatures and heavy background tasks.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Performance",
                    Description = "Check processor clock frequency and active workload."
                }
            };
        }

        if (EvalComponent("cpu") && cpuUsage >= CpuLoadThresholds.Elevated)
        {
            var primary = $"Processor (CPU) is experiencing elevated load ({cpuUsage:0.#}% on {hw.Cpu.Name}).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Elevated CPU Utilization",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = cpuUsage >= 90 ? "High" : "Medium",
                Explanation = primary,
                RecommendedNextStep = "Run the guided inspection below to identify high-CPU processes and safely close non-essential background tasks.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect CPU-intensive processes."
                }
            };
        }

        // 5. Storage utilization check (evaluated before moderate RAM/browser workload so storage issues are not masked)
        if (EvalComponent("storage") && maxDiskUsage >= 80)
        {
            var primary = $"Storage volume usage is elevated at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType}.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = maxDiskUsage >= 90 ? "Low Available Storage Space" : "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 90 ? "High" : "Medium",
                Explanation = primary,
                RecommendedNextStep = "Run the guided inspection below to preview and clean temporary files, browser caches, or Windows Update caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "storage_settings",
                    Label = "Windows Storage Settings",
                    Description = "Inspect volume free space and temporary files."
                }
            };
        }

        // 6. Network connectivity check
        if (EvalComponent("network") && hw.Network != null &&
            (!hw.Network.HasActiveAdapter ||
             (hw.Network.PacketLossPercent ?? 0) >= 5 ||
             (hw.Network.PingLatencyMs ?? 0) >= 150 ||
             (hw.Network.IsWifi && hw.Network.WifiSignalStrength is > 0 and <= 45)))
        {
            var primary = $"Network connectivity issue detected (Ping: {hw.Network.PingLatencyMs?.ToString() ?? "N/A"} ms, Packet Loss: {hw.Network.PacketLossPercent ?? 0:0.#}%).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = primary,
                RecommendedNextStep = "Run the guided inspection below to test network connectivity and flush the Windows DNS resolver cache.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Network & Reliability",
                    Description = "Inspect network adapter status and DNS resolution."
                }
            };
        }

        // 7. Memory & Process Workload checks (strictly when Memory is in scope and genuinely elevated)
        bool explicitMemoryScan = mode == "component" && selectedComponents.Contains("memory");
        var memoryState = MemoryPressureRules.Evaluate(ramUsage, hw.ProcessInsights?.BrowserMemoryMb,
            memLeakWarning, explicitMemoryScan, hw.ProcessInsights != null);

        if (EvalComponent("memory") && memoryState == MemoryPressureState.Unknown)
            return BuildMemoryUnavailableDiagnosis(targetScopeLabels, proof);

        if (EvalComponent("memory") && memoryState == MemoryPressureState.Active)
        {
            var category = MemoryPressureRules.IsValidUsage(ramUsage) && ramUsage >= MemoryPressureRules.HighUsagePercent
                ? "High Memory Pressure"
                : !string.IsNullOrWhiteSpace(memLeakWarning)
                    ? "OS performance degradation"
                    : "Elevated Memory Pressure From Active Workloads";

            var primary = browserMemoryMb >= 2000
                ? $"Memory (RAM) is at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB) with {browserMemoryMb:0.#} MB across {browserProcs} browser processes."
                : $"Memory (RAM) is at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = absentComponentNote,
                DiagnosedCategory = category,
                ActionCategory = ramUsage >= 85 ? "Troubleshoot" : "Maintain",
                ConfidenceLabel = ramUsage >= 85 ? "High" : "Medium",
                Explanation = primary,
                RecommendedNextStep = "Save your work, review which running apps use the most memory, and close only apps you no longer need. File cleanup frees storage, not app memory.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Processes",
                    Description = "Inspect active memory-consuming applications."
                }
            };
        }

        if (EvalComponent("cpu") && (hw.Cpu.UsageAvailable == false || !double.IsFinite(cpuUsage) || cpuUsage is < 0 or > 100))
            return new AutomaticDiagnosisResult
            {
                TargetScope = targetScopeLabels, DiagnosedCategory = "Processor readings unavailable", ActionCategory = "Monitor",
                ConfidenceLabel = "Low", PrimaryResult = "Processor activity could not be measured.",
                Explanation = "A failed activity reading is not zero activity.",
                RecommendedNextStep = "Try the read-only check again when processor activity can be measured.", Proof = proof
            };

        if (EvalComponent("storage") && !hw.AllDisks.Any() && !hw.StorageDrives.Any(d => d.UsagePercent.HasValue))
            return new AutomaticDiagnosisResult
            {
                TargetScope = targetScopeLabels, DiagnosedCategory = "Storage readings unavailable", ActionCategory = "Monitor",
                ConfidenceLabel = "Low", PrimaryResult = "Storage use could not be measured.",
                Explanation = "No measured storage-use value was returned.",
                RecommendedNextStep = "Check again when the drive can be read.", Proof = proof
            };

        // 8. Scoped Component-driven healthy verdict when user explicitly selected specific parts
        if (mode == "component" && selectedComponents.Count > 0)
        {
            if (selectedComponents.Contains("thermal") && !cpuTemp.HasValue)
                return BuildThermalUnavailableDiagnosis(targetScopeLabels, proof);

            return BuildComponentHealthyDiagnosis(
                selectedComponents,
                targetScopeLabels,
                absentComponentNote,
                hw,
                cpuUsage,
                cpuTemp,
                ramUsage,
                maxDiskUsage,
                smartFailing,
                proof);
        }

        var fullPrimary = $"CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%) are operating within normal ranges.";
        return new AutomaticDiagnosisResult
        {
            ComponentStatus = ComponentStatus.Present,
            TargetScope = targetScopeLabels,
            PrimaryResult = fullPrimary,
            IncidentalWarning = null,
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = fullPrimary,
            RecommendedNextStep = "No immediate fix is needed. Continue using the device normally, and run another check if symptoms appear again.",
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = "task_manager",
                Label = "Task Manager",
                Description = "View live Windows resource utilization."
            }
        };
    }

    private static AutomaticDiagnosisResult BuildComponentHealthyDiagnosis(
        HashSet<string> selectedComponents,
        IReadOnlyList<string> targetScopeLabels,
        string? absentComponentNote,
        HardwareProfileDto hw,
        double cpuUsage,
        double? cpuTemp,
        double ramUsage,
        double maxDiskUsage,
        bool smartFailing,
        List<AutomaticDiagnosisProof> proof)
    {
        var componentSummaries = new List<string>();

        if (selectedComponents.Contains("memory"))
        {
            var leakText = string.IsNullOrWhiteSpace(hw.ProcessInsights?.MemoryLeakWarning)
                ? "no app memory warning reported"
                : hw.ProcessInsights!.MemoryLeakWarning!;
            componentSummaries.Add(
                $"Memory (RAM) is operating normally ({hw.Ram.TotalGb:0.#} GB, {ramUsage:0.#}% in use, {leakText}).");
        }

        if (selectedComponents.Contains("cpu"))
        {
            var tempPart = cpuTemp.HasValue ? $", {cpuTemp.Value:0.#}°C" : ", CPU temperature unavailable";
            componentSummaries.Add(
                $"Processor activity is below the workload warning threshold ({cpuUsage:0.#}% load on {hw.Cpu.Name}{tempPart}).");
        }

        if (selectedComponents.Contains("thermal"))
        {
            var tempPart = cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C CPU" : "normal thermal state";
            var gpuTempPart = hw.Gpu.TemperatureCelsius.HasValue ? $", {hw.Gpu.TemperatureCelsius.Value:0.#}°C GPU" : string.Empty;
            componentSummaries.Add(
                $"Thermal / Cooling readings are below RigMD's thermal threshold ({tempPart}{gpuTempPart}, no thermal throttling reported).");
        }

        if (selectedComponents.Contains("storage"))
        {
            componentSummaries.Add(
                $"Storage / SSD / HDD is operating normally ({maxDiskUsage:0.#}% in use on {hw.PrimaryStorageType}, drive warning: {(smartFailing ? "reported" : "not reported")}).");
        }

        if (selectedComponents.Contains("startup"))
        {
            componentSummaries.Add(
                $"Startup / Boot configuration on {hw.PrimaryStorageType} ({maxDiskUsage:0.#}% used) is operating normally.");
        }

        if (selectedComponents.Contains("gpu") || selectedComponents.Contains("display"))
        {
            componentSummaries.Add(
                $"No active device error was reported for the selected graphics hardware ({hw.Gpu.Name}, Driver {hw.Gpu.Driver}, {hw.ConnectedDisplays} connected display(s)).");
        }

        if (selectedComponents.Contains("drivers"))
        {
            componentSummaries.Add(
                "Hardware Drivers are operating normally (0 error codes in Device Manager).");
        }

        if (selectedComponents.Contains("peripherals"))
        {
            componentSummaries.Add(
                "Peripherals / USB controllers are operating normally (0 PnP error codes in Device Manager).");
        }

        if (selectedComponents.Contains("audio"))
        {
            componentSummaries.Add(
                "Audio / Sound controllers are operating normally (0 PnP error codes in Device Manager).");
        }

        if (selectedComponents.Contains("network"))
        {
            var pingStr = hw.Network?.PingLatencyMs.HasValue == true ? $"{hw.Network.PingLatencyMs} ms ping" : "adapter active";
            componentSummaries.Add(
                $"Network / Internet is operating normally ({pingStr}, DNS resolution healthy).");
        }

        if (selectedComponents.Contains("battery") && hw.Battery?.HasBattery == true)
        {
            componentSummaries.Add(
                $"Battery / Power is operating normally ({hw.Battery.EstimatedChargeRemaining}% charge, {hw.Battery.StatusDescription}).");
        }

        if (selectedComponents.Contains("os"))
        {
            componentSummaries.Add(
                $"Operating System ({hw.OsVersion}) is operating normally under power plan '{hw.ActivePowerPlan}'.");
        }

        var primaryResult = componentSummaries.Count > 0
            ? string.Join(" ", componentSummaries)
            : $"Selected component(s) ({string.Join(", ", targetScopeLabels)}) are operating normally.";

        var target = selectedComponents.Contains("storage")
            ? ("storage_settings", "Windows Storage Settings", "Inspect storage volumes and temporary files.")
            : selectedComponents.Contains("startup")
                ? ("startup_apps", "Windows Startup Apps", "Review applications configured to launch at sign-in.")
                : selectedComponents.Contains("gpu") || selectedComponents.Contains("drivers") || selectedComponents.Contains("display") || selectedComponents.Contains("peripherals") || selectedComponents.Contains("audio")
                    ? ("device_manager", "Windows Device Manager", "Inspect display, audio, peripheral, and hardware driver status.")
                    : ("task_manager", "Task Manager", "Inspect live telemetry for the selected components.");

        return new AutomaticDiagnosisResult
        {
            ComponentStatus = ComponentStatus.Present,
            TargetScope = targetScopeLabels,
            PrimaryResult = primaryResult,
            IncidentalWarning = absentComponentNote,
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = primaryResult,
            RecommendedNextStep = "No immediate fix is required for the selected part(s). Run another check if symptoms appear again.",
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = target.Item1,
                Label = target.Item2,
                Description = target.Item3
            }
        };
    }

    private static AutomaticDiagnosisResult BuildScenarioDiagnosis(
        string scenarioId,
        IReadOnlyList<string> targetScopeLabels,
        HardwareProfileDto hw,
        double cpuUsage,
        double? cpuTemp,
        double ramUsage,
        double maxDiskUsage,
        bool smartFailing,
        bool hasDeviceErrors,
        List<AutomaticDiagnosisProof> proof)
    {
        var thermalIssue = hw.Cpu.IsThermallyThrottling ||
            (cpuTemp.HasValue && cpuTemp.Value >= 85) || hw.Gpu.TemperatureCelsius is >= 85;
        var thermalSummary = $"Thermal check recorded CPU temperature {(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "unavailable")}, GPU temperature {(hw.Gpu.TemperatureCelsius.HasValue ? $"{hw.Gpu.TemperatureCelsius.Value:0.#}°C" : "unavailable")}, and processor load {cpuUsage:0.#}% under power plan '{hw.ActivePowerPlan}'.";
        var networkIssue = hw.Network == null ||
            !hw.Network.HasActiveAdapter ||
            !hw.Network.HasIpv4Address ||
            !hw.Network.HasDefaultGateway ||
            !hw.Network.HasDnsServers ||
            !hw.Network.DnsResolutionSucceeded ||
            (hw.Network.PacketLossPercent ?? 0) >= 5 ||
            (hw.Network.PingLatencyMs ?? 0) >= 120 ||
            (hw.Network.IsWifi && hw.Network.WifiSignalStrength is > 0 and <= 45);
        var storageIssue = smartFailing || maxDiskUsage >= 80;
        var validRamUsage = MemoryPressureRules.IsValidUsage(ramUsage);
        var responsivenessIssue = cpuUsage >= CpuLoadThresholds.Responsiveness ||
            (validRamUsage && ramUsage >= MemoryPressureRules.ResponsivenessUsagePercent) || maxDiskUsage >= 80;
        var startupIssue = maxDiskUsage >= 80 || cpuUsage >= CpuLoadThresholds.Startup;
        var batteryIssue = hw.Battery?.HasBattery == true &&
            !hw.Battery.IsCharging &&
            hw.Battery.EstimatedChargeRemaining <= 20;
        var stabilityEvents = hw.StabilityEvents ?? new StabilityEventSnapshotDto();
        var eventLogAvailable = string.IsNullOrWhiteSpace(stabilityEvents.QueryWarning);
        var appCrashEventCount = stabilityEvents.ApplicationCrashEvents?.Count ?? 0;
        var systemCrashEventCount = stabilityEvents.SystemCrashEvents?.Count ?? 0;
        var appCrashSupportingRisk = cpuUsage >= CpuLoadThresholds.Elevated || (validRamUsage && ramUsage >= MemoryPressureRules.HighUsagePercent) || hasDeviceErrors;
        var systemCrashSupportingRisk = cpuUsage >= CpuLoadThresholds.SystemStabilityRisk || hasDeviceErrors;

        return scenarioId switch
        {
            "slow-system" or "stuttering-freezing" when !responsivenessIssue && !validRamUsage =>
                BuildMemoryUnavailableDiagnosis(targetScopeLabels, proof),
            "slow-system" or "stuttering-freezing" when !responsivenessIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active performance bottleneck detected. Live scan recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}%, and Storage at {maxDiskUsage:0.#}%.",
                "Task Manager - Processes",
                "task_manager",
                "Inspect active CPU, memory, and storage workload if the symptom happens again.",
                proof),
            "slow-system" or "stuttering-freezing" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"System responsiveness check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and Storage at {maxDiskUsage:0.#}%.",
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Maintain",
                ConfidenceLabel = ramUsage >= 65 || cpuUsage >= 55 ? "High" : "Medium",
                Explanation = $"System responsiveness check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and Storage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType}.",
                RecommendedNextStep = "Run the guided inspection below to check active CPU, memory, and storage workloads and preview safe cache cleanup.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Processes",
                    Description = "Inspect active background processes and system resource usage."
                }
            },
            "slow-boot" when !startupIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active boot-resource issue detected. Startup check recorded primary storage at {maxDiskUsage:0.#}% and CPU baseline at {cpuUsage:0.#}%.",
                "Windows Startup Apps",
                "startup_apps",
                "Review applications configured to launch at Windows sign-in if slow boot happens again.",
                proof),
            "slow-boot" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Startup check recorded primary storage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and CPU startup baseline at {cpuUsage:0.#}%.",
                DiagnosedCategory = "Boot and startup failure",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 80 || cpuUsage >= CpuLoadThresholds.Startup ? "High" : "Medium",
                Explanation = $"Startup performance check recorded boot drive usage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} under power plan '{hw.ActivePowerPlan}'.",
                RecommendedNextStep = "Review startup applications in Windows Settings or run the guided inspection below to check startup impact and clear temporary caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "startup_apps",
                    Label = "Windows Startup Apps",
                    Description = "Review applications configured to launch at Windows sign-in."
                }
            },
            "app-crashes" when eventLogAvailable && appCrashEventCount == 0 && !appCrashSupportingRisk && !validRamUsage =>
                BuildMemoryUnavailableDiagnosis(targetScopeLabels, proof),
            "app-crashes" when eventLogAvailable && appCrashEventCount == 0 && !appCrashSupportingRisk => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active application crash issue detected. Windows Event Logs show 0 recent application crash event(s), CPU is {cpuUsage:0.#}%, and RAM is {ramUsage:0.#}%.",
                "Windows Reliability Monitor",
                "reliability_monitor",
                "Review Reliability Monitor again if an app closes unexpectedly.",
                proof),
            "app-crashes" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = appCrashEventCount > 0
                    ? $"Windows Event Logs show {appCrashEventCount} recent application crash event(s). CPU is {cpuUsage:0.#}% and RAM is {ramUsage:0.#}%."
                    : eventLogAvailable
                        ? $"No recent application crash event is visible, but supporting risk signals are still high: CPU {cpuUsage:0.#}%, RAM {ramUsage:0.#}%, Device Manager warnings {(hw.DeviceErrors?.Count ?? 0)}."
                        : $"RigMD could not verify recent application crash events from Windows Event Logs. CPU is {cpuUsage:0.#}% and RAM is {ramUsage:0.#}%.",
                DiagnosedCategory = appCrashEventCount > 0 || !eventLogAvailable
                    ? "Application crash history requires review"
                    : "Application stability risk needs review",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = appCrashEventCount > 0 || hasDeviceErrors || cpuUsage >= CpuLoadThresholds.Elevated || ramUsage >= 85 ? "High" : "Medium",
                Explanation = appCrashEventCount > 0
                    ? $"Windows Event Logs show {appCrashEventCount} recent application crash event(s). Reliability Monitor can show which app failed and when."
                    : eventLogAvailable
                        ? $"Windows Event Logs do not show a new application crash right now, but live CPU, memory, or driver signals may still contribute to app freezes or forced closes."
                        : $"RigMD could not confirm Windows Event Log crash evidence right now, so the application stability check remains in review instead of being marked clear.",
                RecommendedNextStep = appCrashEventCount > 0
                    ? "Open Windows Reliability Monitor to identify the failing app, or run the guided inspection below to check Windows error logs."
                    : "Reduce the highlighted risk signal or run the guided inspection below, then recheck current status.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Reliability Monitor",
                    Description = "Review recent application crashes and fault events."
                }
            },
            "blue-screen-crash" when eventLogAvailable && systemCrashEventCount == 0 && !systemCrashSupportingRisk => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active Windows stop-error issue detected. Windows Event Logs show 0 recent system crash event(s), CPU is {cpuUsage:0.#}%, and Device Manager reports {(hw.DeviceErrors?.Count ?? 0)} active device error(s).",
                "Windows Reliability Monitor",
                "reliability_monitor",
                "Review Reliability Monitor again if the PC restarts unexpectedly or shows a blue screen.",
                proof),
            "blue-screen-crash" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = systemCrashEventCount > 0
                    ? $"Windows Event Logs show {systemCrashEventCount} recent system crash or restart event(s). CPU is {cpuUsage:0.#}% and Device Manager reports {(hw.DeviceErrors?.Count ?? 0)} active device error(s)."
                    : $"System crash check needs review because supporting risk signals remain active: CPU {cpuUsage:0.#}% and Device Manager warnings {(hw.DeviceErrors?.Count ?? 0)}.",
                DiagnosedCategory = "System crash or stop error requires review",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = systemCrashEventCount > 0 || hasDeviceErrors || cpuUsage >= CpuLoadThresholds.SystemStabilityRisk ? "High" : "Medium",
                Explanation = systemCrashEventCount > 0
                    ? $"Windows Event Logs show {systemCrashEventCount} recent system crash or unexpected restart event(s). Checking stop-error logs and system file integrity is recommended."
                    : $"No recent stop-error event is visible, but live driver or processor signals still need review before the system crash scenario is considered clear.",
                RecommendedNextStep = systemCrashEventCount > 0
                    ? "Check Windows Reliability Monitor for stop errors or run the guided inspection below to check crash logs and system files."
                    : "Review the highlighted risk signal, then recheck current status.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Reliability Monitor",
                    Description = "Review Windows stop error and unexpected restart events."
                }
            },
            "driver-error" when !hasDeviceErrors => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active driver conflict detected. Device Manager reports 0 active device error(s) for the current hardware scan, including {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}).",
                "Windows Device Manager",
                "device_manager",
                "Check driver status again if a device disappears, fails, or shows a warning icon.",
                proof),
            "driver-error" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Driver check completed for {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}) with {hw.DeviceErrors?.Count ?? 0} active device error(s).",
                DiagnosedCategory = "Driver conflict",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = hasDeviceErrors ? "High" : "Medium",
                Explanation = $"Driver check completed for {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}) with {hw.DeviceErrors?.Count ?? 0} PnP error code(s) currently active in Device Manager.",
                RecommendedNextStep = "Open Windows Device Manager or run the guided inspection below to check for driver warnings and system event errors.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "device_manager",
                    Label = "Windows Device Manager",
                    Description = "Inspect hardware drivers and device status."
                }
            },
            "no-display" when !hasDeviceErrors && hw.ConnectedDisplays > 0 => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active display-driver issue detected. Display check completed for {hw.Gpu.Name} across {hw.ConnectedDisplays} connected display(s).",
                "Device Manager - Display Adapters",
                "device_manager",
                "Check display adapter status again if flicker, black-screen behavior, or display instability returns.",
                proof),
            "no-display" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Display check completed for {hw.Gpu.Name} across {hw.ConnectedDisplays} connected display(s).",
                DiagnosedCategory = "Display driver behavior",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = hasDeviceErrors ? "High" : "Medium",
                Explanation = $"Display check completed for {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}) across {hw.ConnectedDisplays} connected display(s).",
                RecommendedNextStep = "Check display adapter status in Device Manager or run the guided inspection below to check graphics telemetry and refresh the Windows shell.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "device_manager",
                    Label = "Device Manager - Display Adapters",
                    Description = "Inspect display adapter driver status."
                }
            },
            "overheating-loud-fan" when !thermalIssue && !cpuTemp.HasValue =>
                BuildThermalUnavailableDiagnosis(targetScopeLabels, proof),
            "overheating-loud-fan" when !thermalIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active overheating detected. Thermal check recorded CPU load at {cpuUsage:0.#}% ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "no thermal throttling reported")}) under power plan '{hw.ActivePowerPlan}'.",
                "Task Manager - Performance",
                "task_manager",
                "Check fan airflow and vents if heat or fan noise returns, or run another thermal check while the issue is happening.",
                proof),
            "overheating-loud-fan" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = thermalSummary,
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = thermalSummary,
                RecommendedNextStep = "Check fan airflow and vents, or run the guided inspection below to check processor temperatures and close heat-generating tasks.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Performance",
                    Description = "Inspect processor utilization and thermal load."
                }
            },
            "network-problem" or "network-issue" when !networkIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active network issue detected. Network check recorded ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                "Windows Network Diagnostics",
                "reliability_monitor",
                "Run another network check if connection drops, DNS failures, or high latency happen again.",
                proof),
            "network-problem" or "network-issue" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Network check recorded ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = hw.Network != null && (!hw.Network.HasActiveAdapter || !hw.Network.DnsResolutionSucceeded || (hw.Network.PingLatencyMs ?? 0) >= 120) ? "High" : "Medium",
                Explanation = $"Network check recorded adapter ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                RecommendedNextStep = "Run the guided inspection below to test network connectivity and flush the Windows DNS resolver cache.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Network Diagnostics",
                    Description = "Inspect network adapter status and DNS cache."
                }
            },
            "storage-problem" or "disk-full" when !storageIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active storage issue detected. Storage check recorded primary volume usage at {maxDiskUsage:0.#}% ({(smartFailing ? "S.M.A.R.T. warning active" : "no drive-health warning reported")}) across {hw.StorageDrives.Count} drive(s).",
                "Windows Storage Settings",
                "storage_settings",
                "Review storage again if save/load behavior slows down or free space drops below the warning threshold.",
                proof),
            "storage-problem" or "disk-full" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Storage check recorded primary volume usage at {maxDiskUsage:0.#}% ({(smartFailing ? "S.M.A.R.T. warning active" : "S.M.A.R.T. healthy")}) across {hw.StorageDrives.Count} drive(s).",
                DiagnosedCategory = smartFailing ? "Storage health behavior" : maxDiskUsage >= 90 ? "Low Available Storage Space" : "Elevated Storage Utilization",
                ActionCategory = smartFailing ? "Escalate" : "Maintain",
                ConfidenceLabel = smartFailing || maxDiskUsage >= 80 ? "High" : "Medium",
                Explanation = $"Storage check recorded primary volume usage at {maxDiskUsage:0.#}% across {hw.StorageDrives.Count} drive(s) ({(smartFailing ? "S.M.A.R.T. warning reported" : "S.M.A.R.T. healthy")}).",
                RecommendedNextStep = smartFailing
                    ? "Back up important files immediately, then review drive health in Windows Storage Settings."
                    : "Run the guided inspection below to check drive health and preview temporary file cleanup.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "storage_settings",
                    Label = "Windows Storage Settings",
                    Description = "Inspect disk space and temporary files."
                }
            },
            "rapid-battery-drain" when hw.Battery?.HasBattery == true && !batteryIssue => BuildScenarioHealthyDiagnosis(
                targetScopeLabels,
                $"No active battery or power issue detected. Battery check recorded {hw.Battery.EstimatedChargeRemaining}% charge ({hw.Battery.StatusDescription}) under power plan '{hw.ActivePowerPlan}'.",
                "Task Manager - Power Usage",
                "task_manager",
                "Review battery and power usage again if drain happens while unplugged.",
                proof),
            "rapid-battery-drain" => new AutomaticDiagnosisResult
            {
                ComponentStatus = hw.Battery?.HasBattery == true ? ComponentStatus.Present : ComponentStatus.NotPresent,
                TargetScope = targetScopeLabels,
                PrimaryResult = hw.Battery?.HasBattery == true
                    ? $"Battery check recorded {hw.Battery.EstimatedChargeRemaining}% charge ({hw.Battery.StatusDescription}) under power plan '{hw.ActivePowerPlan}'."
                    : "No battery detected on this device",
                DiagnosedCategory = hw.Battery?.HasBattery == true ? "Battery and power configuration" : "Component Not Present",
                ActionCategory = hw.Battery?.HasBattery == true ? "Maintain" : "Monitor",
                ConfidenceLabel = "High",
                Explanation = hw.Battery?.HasBattery == true
                    ? $"Battery check recorded {hw.Battery.EstimatedChargeRemaining}% charge ({hw.Battery.StatusDescription}) with CPU load at {cpuUsage:0.#}% under power plan '{hw.ActivePowerPlan}'."
                    : "No battery detected on this desktop device.",
                RecommendedNextStep = "Review active power plan settings or run the guided inspection below to check battery drain and background processor activity.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Power Usage",
                    Description = "Inspect applications with high power usage."
                }
            },
            _ => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Scenario check completed with CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}%, and Storage at {maxDiskUsage:0.#}%.",
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Scenario check completed with CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}%, and Storage at {maxDiskUsage:0.#}%.",
                RecommendedNextStep = "Run the guided inspection below to review live system telemetry and safe maintenance options.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect live system resources."
                }
            }
        };
    }

    private static AutomaticDiagnosisResult BuildMemoryUnavailableDiagnosis(
        IReadOnlyList<string> targetScopeLabels,
        List<AutomaticDiagnosisProof> proof) => new()
    {
        ComponentStatus = ComponentStatus.Present,
        TargetScope = targetScopeLabels,
        PrimaryResult = "Memory pressure could not be verified because required RAM or process data is unavailable.",
        DiagnosedCategory = "Memory telemetry unavailable",
        ActionCategory = "Monitor",
        ConfidenceLabel = "Low",
        Explanation = "Missing or invalid memory readings cannot confirm normal RAM usage or active memory pressure.",
        RecommendedNextStep = "Refresh the device data and recheck when RAM and process readings are available.",
        Proof = proof,
        VerificationTarget = new AutomaticVerificationTarget
        {
            Target = "task_manager", Label = "Task Manager - Memory",
            Description = "Review physical memory usage and active applications."
        }
    };

    private static AutomaticDiagnosisResult BuildThermalUnavailableDiagnosis(
        IReadOnlyList<string> targetScopeLabels,
        List<AutomaticDiagnosisProof> proof) => new()
    {
        ComponentStatus = ComponentStatus.Present,
        TargetScope = targetScopeLabels,
        PrimaryResult = "CPU temperature could not be verified because an identified CPU sensor is unavailable.",
        DiagnosedCategory = "Thermal telemetry unavailable",
        ActionCategory = "Monitor",
        ConfidenceLabel = "Low",
        Explanation = "Processor workload is not a temperature measurement. Missing sensor data cannot confirm normal thermals or overheating.",
        RecommendedNextStep = "Review available sensor support and recheck when a CPU sensor reading is available. Do not infer a temperature from processor load or GPU readings.",
        Proof = proof,
        VerificationTarget = new AutomaticVerificationTarget
        {
            Target = "task_manager",
            Label = "Task Manager - Performance",
            Description = "Review processor activity separately; Task Manager does not verify CPU temperature."
        }
    };

    private static AutomaticDiagnosisResult BuildScenarioHealthyDiagnosis(
        IReadOnlyList<string> targetScopeLabels,
        string primaryResult,
        string verificationLabel,
        string verificationTarget,
        string verificationDescription,
        List<AutomaticDiagnosisProof> proof)
    {
        return new AutomaticDiagnosisResult
        {
            ComponentStatus = ComponentStatus.Present,
            TargetScope = targetScopeLabels,
            PrimaryResult = primaryResult,
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = primaryResult,
            RecommendedNextStep = "No immediate fix is required for the selected area. " + verificationDescription,
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = verificationTarget,
                Label = verificationLabel,
                Description = verificationDescription
            }
        };
    }

    private static List<AutomaticDiagnosisProof> BuildTelemetryProof(
        HardwareProfileDto hw,
        string mode,
        string scenarioId,
        HashSet<string> selectedComponents,
        double cpuUsage,
        double? cpuTemp,
        double ramUsage,
        double maxDiskUsage,
        bool smartFailing,
        int deviceErrorCount,
        double browserMemoryMb,
        int browserProcs,
        DateTimeOffset? capturedAt, DateTimeOffset? eventsSince)
    {
        var activeScopeKeys = ResolveScopeKeys(mode, scenarioId, selectedComponents);
        bool Include(params string[] keys) =>
            activeScopeKeys == null || keys.Any(activeScopeKeys.Contains);

        var proof = new List<AutomaticDiagnosisProof>();

        if (Include("cpu", "thermal", "os"))
        {
            var validLoad = hw.Cpu.UsageAvailable != false && double.IsFinite(cpuUsage) && cpuUsage is >= 0 and <= 100;
            var staleLoad = hw.Cpu.UsageCapturedAtUtc.HasValue && capturedAt.HasValue &&
                (capturedAt.Value - hw.Cpu.UsageCapturedAtUtc.Value > TimeSpan.FromSeconds(5) ||
                 hw.Cpu.UsageCapturedAtUtc.Value - capturedAt.Value > TimeSpan.FromSeconds(2));
            validLoad &= !staleLoad;
            var loadThreshold = scenarioId is "slow-system" or "stuttering-freezing" ? CpuLoadThresholds.Responsiveness
                : scenarioId == "slow-boot" ? CpuLoadThresholds.Startup
                : scenarioId == "blue-screen-crash" ? CpuLoadThresholds.SystemStabilityRisk : CpuLoadThresholds.Elevated;
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "cpu_usage", Label = "Processor (CPU)",
                CapturedAt = hw.Cpu.UsageCapturedAtUtc,
                Value = validLoad ? $"{cpuUsage:0.#}%" : "Unavailable",
                Status = !validLoad ? "unknown" : scenarioId == "overheating-loud-fan" ||
                    mode == "component" && selectedComponents.Contains("thermal") && !selectedComponents.Contains("cpu")
                    ? "info" : cpuUsage >= loadThreshold ? "high" : "normal",
                Meaning = staleLoad ? "The processor activity sample is too old or has an invalid time. Recheck for a fresh measurement."
                    : $"Processor activity measured for {hw.Cpu.Name}. Workload does not measure temperature. Activity source: {hw.Cpu.UsageSource}."
            });
            var validTemp = cpuTemp.HasValue && double.IsFinite(cpuTemp.Value) && cpuTemp.Value is > 0 and <= 125;
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "cpu_temperature", Label = "Processor temperature",
                Value = validTemp ? $"{cpuTemp:0.#}°C" : "Unavailable",
                Status = !validTemp ? "unknown" : cpuTemp >= 85 ? "high" : "normal",
                CapturedAt = hw.Cpu.TemperatureCapturedAtUtc,
                Meaning = $"CPU temperature source: {hw.Cpu.TemperatureSource}. Missing temperature is not inferred from activity."
            });
            if (hw.Cpu.IsThermallyThrottling)
                proof.Add(new AutomaticDiagnosisProof
                {
                    MetricId = "cpu_throttling", Label = "Heat-related slowdown report",
                    Value = "Reported", Status = "high",
                    Meaning = "A heat-related slowdown was reported independently of the temperature reading."
                });
        }

        if (Include("memory"))
        {
            bool showBrowserDetail = (mode == "component" && selectedComponents.Contains("memory") && browserProcs > 0)
                || (browserMemoryMb >= 2200 && browserProcs > 0);

            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "memory_usage", Label = "Physical Memory (RAM)",
                Value = hw.Ram.UsageAvailable != false && MemoryPressureRules.IsValidUsage(ramUsage)
                    ? $"{ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB)" : "Unavailable",
                Status = hw.Ram.UsageAvailable == false || !MemoryPressureRules.IsValidUsage(ramUsage) ? "unknown"
                    : ramUsage >= MemoryPressureRules.HighUsagePercent ? "high" : "normal",
                Meaning = showBrowserDetail
                    ? $"Active browser workload: {browserMemoryMb:0.#} MB across {browserProcs} processes."
                    : "Physical RAM utilization across active Windows processes."
            });
            if (!string.IsNullOrWhiteSpace(hw.ProcessInsights?.MemoryLeakWarning))
                proof.Add(new AutomaticDiagnosisProof
                {
                    MetricId = "memory_app_warning", Label = "Application memory warning", Value = hw.ProcessInsights.MemoryLeakWarning,
                    Status = "high", Meaning = "The process inspection reported unusual application memory behavior."
                });
        }

        if (Include("storage", "startup"))
        {
            var hasUsage = (hw.AllDisks.Any() || hw.StorageDrives.Any(d => d.UsagePercent.HasValue)) &&
                double.IsFinite(maxDiskUsage) && maxDiskUsage is >= 0 and <= 100;
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "storage_usage", Label = "Storage space",
                Value = hasUsage ? $"{maxDiskUsage:0.#}% used" : "Unavailable",
                Status = !hasUsage ? "unknown" : maxDiskUsage >= 80 ? "high" : "normal",
                Meaning = "Space used by Windows and saved files. This does not measure drive health."
            });
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "storage_health", Label = "Drive-health warnings",
                Value = smartFailing ? "Warning reported" : hw.StorageDrives.Count > 0 ? "No warning reported" : "Unavailable",
                Status = smartFailing ? "high" : hw.StorageDrives.Count > 0 ? "info" : "unknown",
                Meaning = "Absence of a reported warning is not a comprehensive drive-health test."
            });
        }

        if (Include("gpu", "display", "drivers", "peripherals", "audio", "thermal"))
        {
            var driverErrorInScope = activeScopeKeys == null ||
                activeScopeKeys.Contains("drivers") ||
                activeScopeKeys.Contains("display") ||
                activeScopeKeys.Contains("peripherals") ||
                activeScopeKeys.Contains("audio") ||
                (mode == "component" && selectedComponents.Contains("gpu"));
            if (driverErrorInScope)
            {
                var scopedErrors = DeviceErrorAttribution.SelectForScope(hw.DeviceErrors, mode, scenarioId, selectedComponents);
                proof.Add(new AutomaticDiagnosisProof
                {
                    MetricId = "graphics_devices", Label = "Graphics & Drivers",
                    Value = hw.DeviceErrorsAvailable != true ? "Unavailable" : $"{scopedErrors.Count} device error(s) reported in the checked scope",
                    Status = hw.DeviceErrorsAvailable != true ? "unknown" : "info",
                    Meaning = "Device Manager error codes describe device status, not a confirmed driver conflict or a functional test."
                });
                for (var i = 0; i < scopedErrors.Count; i++)
                {
                    var error = scopedErrors[i];
                    proof.Add(new AutomaticDiagnosisProof
                    {
                        MetricId = DeviceErrorAttribution.MetricId(error), Label = error.Name,
                        Value = $"Device Manager code {error.ErrorCode}", Status = "high",
                        Meaning = error.Description + " Review this specific code before choosing a repair."
                    });
                }
            }
            else
                proof.Add(new AutomaticDiagnosisProof
                {
                    MetricId = "graphics_context", Label = "Graphics Thermal Context",
                    Value = hw.Gpu.Name, Status = "info",
                    Meaning = "Graphics hardware information only. This is not a temperature measurement."
                });
            if (activeScopeKeys == null || (activeScopeKeys.Contains("gpu") || activeScopeKeys.Contains("thermal")) &&
                scenarioId is not ("driver-error" or "no-display" or "blue-screen-crash"))
            {
            var gpuTemp = hw.Gpu.TemperatureCelsius;
            var validGpuTemp = gpuTemp.HasValue && double.IsFinite(gpuTemp.Value) && gpuTemp.Value is > 0 and <= 125;
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "gpu_temperature", Label = "Graphics temperature",
                Value = validGpuTemp ? $"{gpuTemp:0.#}°C" : "Unavailable",
                Status = !validGpuTemp ? "unknown" : gpuTemp >= 85 ? "high" : "normal",
                Meaning = "Graphics temperature must come from its own sensor, not the processor workload."
            });
            }
        }

        if (Include("network")) proof.AddRange(NetworkEvidence.Read(hw.Network));

        if (Include("battery") && hw.Battery?.HasBattery == true)
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = "battery_charge", Label = "Battery & Power State",
                Value = hw.Battery.ChargeAvailable == false || hw.Battery.EstimatedChargeRemaining is < 0 or > 100
                    ? "Unavailable" : $"{hw.Battery.EstimatedChargeRemaining}% ({hw.Battery.StatusDescription})",
                Status = hw.Battery.ChargeAvailable == false || hw.Battery.EstimatedChargeRemaining is < 0 or > 100 ? "unknown"
                    : hw.Battery.EstimatedChargeRemaining <= 20 && !hw.Battery.IsCharging ? "elevated" : "normal",
                Meaning = $"Active Power Plan: {hw.ActivePowerPlan}. Charge does not establish battery health."
            });
        }
        else if (Include("battery") && hw.Battery?.QuerySucceeded != true)
        {
            proof.Add(new AutomaticDiagnosisProof { MetricId = "battery_charge", Label = "Battery detection", Value = "Unavailable", Status = "unknown", Meaning = "Battery information could not be obtained. This does not confirm the battery is absent." });
        }
        else if (Include("os", "startup"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "OS & Power State",
                Value = hw.Battery?.HasBattery == true
                    ? $"Battery {hw.Battery.EstimatedChargeRemaining}% ({hw.Battery.StatusDescription})"
                    : $"AC Power ({hw.ActivePowerPlan})",
                Status = "info",
                Meaning = $"{hw.OsVersion} on {hw.DeviceName} ({hw.DeviceType})."
            });
        }

        if (mode == "scenario" && scenarioId is "app-crashes" or "blue-screen-crash")
        {
            var stabilityEvents = hw.StabilityEvents ?? new StabilityEventSnapshotDto();
            var eventLogAvailable = stabilityEvents.QuerySucceeded == true && string.IsNullOrWhiteSpace(stabilityEvents.QueryWarning);
            var relevantEvents = scenarioId == "app-crashes"
                ? stabilityEvents.ApplicationCrashEvents
                : stabilityEvents.SystemCrashEvents;
            var timedEvents = relevantEvents ?? new List<WindowsEventSummaryDto>();
            if (eventsSince.HasValue && (eventsSince.Value > DateTimeOffset.UtcNow || eventsSince.Value < DateTimeOffset.UtcNow.AddDays(-7) ||
                timedEvents.Any(e => !e.TimeUtc.HasValue || e.TimeUtc > DateTimeOffset.UtcNow))) eventLogAvailable = false;
            var eventCount = timedEvents.Count(e => !eventsSince.HasValue ||
                e.TimeUtc.HasValue && e.TimeUtc >= eventsSince.Value && e.TimeUtc <= DateTimeOffset.UtcNow);
            var label = (scenarioId == "app-crashes" ? "Application crash records" : "System crash or unexpected restart records") +
                (eventsSince.HasValue ? " since diagnosis" : " (last 7 days, up to 40 matching records)");

            proof.Add(new AutomaticDiagnosisProof
            {
                MetricId = scenarioId == "app-crashes" ? "application_crash_history" : "system_crash_history",
                WindowKind = eventsSince.HasValue ? "since_diagnosis" : "rolling_history",
                WindowStart = eventsSince ?? (capturedAt ?? DateTimeOffset.UtcNow).AddDays(-7),
                WindowEnd = capturedAt ?? DateTimeOffset.UtcNow,
                Label = label,
                Value = eventLogAvailable || eventCount > 0
                    ? $"{eventCount} found"
                    : "Could not verify",
                Status = eventCount > 0 ? "high" : !eventLogAvailable ? "unknown" : "normal",
                Meaning = eventLogAvailable
                    ? $"Checked Windows Event Logs for {label.ToLowerInvariant()}. This is a bounded event query, not a cause analysis or proof that the problem is happening now."
                    : stabilityEvents.QueryWarning ?? "Windows Event Logs could not be checked."
            });
            if (!eventLogAvailable && eventCount > 0)
                proof.Add(new AutomaticDiagnosisProof { MetricId = "event_query_coverage", Label = "Error-record coverage", Value = "Unavailable", Status = "unknown", Meaning = "Some matching records were obtained, but complete coverage of the requested time window could not be confirmed." });
        }

        foreach (var item in proof) item.CapturedAt ??= capturedAt;
        return proof;
    }

    private static HashSet<string>? ResolveScopeKeys(
        string mode,
        string scenarioId,
        HashSet<string> selectedComponents)
    {
        if (mode == "component" && selectedComponents.Count > 0)
        {
            return selectedComponents;
        }

        if (mode == "scenario" && !string.IsNullOrWhiteSpace(scenarioId))
        {
            return scenarioId switch
            {
                "overheating-loud-fan" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "thermal", "gpu" },
                "driver-error" or "no-display" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gpu", "display", "drivers" },
                "network-problem" or "network-issue" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "network" },
                "storage-problem" or "disk-full" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "storage" },
                "rapid-battery-drain" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "battery", "cpu", "os" },
                "slow-boot" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "storage", "startup", "os" },
                "app-crashes" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "memory", "os" },
                "blue-screen-crash" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "gpu", "drivers", "os" },
                "slow-system" or "stuttering-freezing" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "memory", "storage" },
                _ => null
            };
        }

        return null;
    }
}
