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
                if (selectedComponents.Count == 1)
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
                        Explanation = "No battery detected on this device. This desktop computer operates on direct AC power and does not have a physical battery to scan.",
                        RecommendedNextStep = "Select present hardware components (such as Processor, Memory, Storage, or Network) to run a targeted check.",
                        Proof = new List<AutomaticDiagnosisProof>
                        {
                            new()
                            {
                                Label = "Battery / Power",
                                Value = "Not Present (Desktop PC)",
                                Status = "normal",
                                Meaning = "No battery detected on this device via Win32_Battery."
                            }
                        }
                    };
                }

                selectedComponents.Remove("battery");
                targetScopeLabels = selectedComponents.Select(DiagnosticScopeMapper.GetComponentDisplayName).ToList();
                absentComponentNote = "Battery / Power was skipped because no battery was detected on this desktop device.";
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
        var ramUsage = hw.Ram.UsagePercent;
        var maxDiskUsage = hw.AllDisks.Any()
            ? hw.AllDisks.Max(d => d.UsagePercent)
            : hw.StorageDrives.FirstOrDefault()?.UsagePercent ?? 0;
        var smartFailing = hw.StorageDrives.Any(d => d.IsFailingSmart);
        var hasDeviceErrors = hw.DeviceErrors is { Count: > 0 };
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
            hasDeviceErrors,
            browserMemoryMb,
            browserProcs);

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

        if (EvalComponent("drivers", "gpu", "display") && hasDeviceErrors)
        {
            var firstErr = hw.DeviceErrors.First();
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
            cpuUsage >= 85 && ramUsage >= 85 && maxDiskUsage >= 85)
        {
            var primary = $"Combined resource exhaustion detected across CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%).";
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
        if (EvalComponent("cpu", "thermal") && (hw.Cpu.IsThermallyThrottling || (cpuTemp.HasValue && cpuTemp.Value >= 85)))
        {
            var primary = $"Thermal pressure detected on {hw.Cpu.Name} ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermal throttling active")}).";
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

        if (EvalComponent("cpu") && cpuUsage >= 80)
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

        // 5. Memory & Process Workload checks (strictly for Full Scan or explicit Memory/OS Component Scan)
        if (EvalComponent("memory", "os") && (!string.IsNullOrWhiteSpace(memLeakWarning) || ramUsage >= 80 || (ramUsage >= 68 && browserMemoryMb >= 1500)))
        {
            var category = ramUsage >= 80
                ? "High Memory Pressure"
                : !string.IsNullOrWhiteSpace(memLeakWarning)
                    ? "OS performance degradation"
                    : "Elevated Memory Pressure From Active Workloads";

            var primary = $"Memory (RAM) is at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB) with {browserMemoryMb:0.#} MB across {browserProcs} browser processes.";
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
                RecommendedNextStep = "Close unused browser tabs or run the guided inspection below to review memory-heavy apps and clear caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Processes",
                    Description = "Inspect active memory-consuming applications."
                }
            };
        }

        // 6. Storage utilization check
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

        // 7. Network connectivity check
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

        // 8. Scoped Component-driven healthy verdict when user explicitly selected specific parts
        if (mode == "component" && selectedComponents.Count > 0)
        {
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
            RecommendedNextStep = "No immediate fix is needed. You can run the guided inspection below for a deeper check or routine cleanup.",
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
                ? "no leaks detected"
                : hw.ProcessInsights!.MemoryLeakWarning!;
            componentSummaries.Add(
                $"Memory (RAM) is operating normally ({hw.Ram.TotalGb:0.#} GB, {ramUsage:0.#}% in use, {leakText}).");
        }

        if (selectedComponents.Contains("cpu"))
        {
            var tempPart = cpuTemp.HasValue ? $", {cpuTemp.Value:0.#}°C" : ", thermals normal";
            componentSummaries.Add(
                $"Processor (CPU) is operating normally ({cpuUsage:0.#}% load on {hw.Cpu.Name}{tempPart}).");
        }

        if (selectedComponents.Contains("storage"))
        {
            componentSummaries.Add(
                $"Storage / SSD / HDD is operating normally ({maxDiskUsage:0.#}% in use on {hw.PrimaryStorageType}, S.M.A.R.T. {(smartFailing ? "warning" : "healthy")}).");
        }

        if (selectedComponents.Contains("gpu") || selectedComponents.Contains("display"))
        {
            componentSummaries.Add(
                $"GPU / Graphics ({hw.Gpu.Name}, Driver {hw.Gpu.Driver}) is operating normally across {hw.ConnectedDisplays} display(s).");
        }

        if (selectedComponents.Contains("drivers"))
        {
            componentSummaries.Add(
                "Hardware Drivers are operating normally (0 error codes in Device Manager).");
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
            : selectedComponents.Contains("gpu") || selectedComponents.Contains("drivers") || selectedComponents.Contains("display")
                ? ("device_manager", "Windows Device Manager", "Inspect display and hardware driver status.")
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
            RecommendedNextStep = "No immediate fix is required for the selected part(s). You can run the guided inspection below for a deeper check.",
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
        return scenarioId switch
        {
            "slow-system" or "stuttering-freezing" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"System responsiveness check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and Storage at {maxDiskUsage:0.#}%.",
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Maintain",
                ConfidenceLabel = ramUsage >= 65 || cpuUsage >= 55 ? "High" : "Medium",
                Explanation = $"System responsiveness check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and {hw.ProcessInsights?.BrowserMemoryMb ?? 0:0.#} MB in browser processes.",
                RecommendedNextStep = "Run the guided inspection below to check active CPU/memory workloads and preview safe cache cleanup.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Processes",
                    Description = "Inspect active background processes and memory usage."
                }
            },
            "slow-boot" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Startup check recorded primary storage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and RAM at {ramUsage:0.#}%.",
                DiagnosedCategory = "Boot and startup failure",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 80 || ramUsage >= 75 ? "High" : "Medium",
                Explanation = $"Startup performance check recorded storage usage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and RAM usage at {ramUsage:0.#}%.",
                RecommendedNextStep = "Review startup applications in Windows Settings or run the guided inspection below to check startup impact and clear temporary caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "startup_apps",
                    Label = "Windows Startup Apps",
                    Description = "Review applications configured to launch at Windows sign-in."
                }
            },
            "app-crashes" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Application stability check recorded CPU at {cpuUsage:0.#}% and RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB).",
                DiagnosedCategory = "Application crash history requires review",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = ramUsage >= 80 || cpuUsage >= 80 ? "High" : "Medium",
                Explanation = $"Application stability check recorded CPU at {cpuUsage:0.#}% and RAM at {ramUsage:0.#}%. Reviewing recent application fault entries in Windows Reliability Monitor and Event Logs helps pinpoint which program failed.",
                RecommendedNextStep = "Review recent application crash events in Windows Reliability Monitor or run the guided inspection below to check Windows error logs.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Reliability Monitor",
                    Description = "Review recent application crashes and fault events."
                }
            },
            "blue-screen-crash" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"System crash check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}%, and {hw.DeviceErrors?.Count ?? 0} active device error(s).",
                DiagnosedCategory = "System crash or stop error requires review",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = hasDeviceErrors || ramUsage >= 85 ? "High" : "Medium",
                Explanation = $"System crash check recorded CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}%, and {hw.DeviceErrors?.Count ?? 0} Device Manager error(s). Checking Windows stop error logs and driver status is recommended.",
                RecommendedNextStep = "Check Windows Reliability Monitor for stop errors or run the guided inspection below to check crash logs and system files.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Reliability Monitor",
                    Description = "Review Windows stop error and unexpected restart events."
                }
            },
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
            "overheating-loud-fan" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Thermal check recorded CPU load at {cpuUsage:0.#}% ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : hw.Cpu.IsThermallyThrottling ? "throttling active" : "thermals nominal")}) under power plan '{hw.ActivePowerPlan}'.",
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = hw.Cpu.IsThermallyThrottling || (cpuTemp.HasValue && cpuTemp.Value >= 80) || cpuUsage >= 75 ? "High" : "Medium",
                Explanation = $"Thermal check recorded CPU load at {cpuUsage:0.#}% ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermals nominal")}) under power plan '{hw.ActivePowerPlan}'.",
                RecommendedNextStep = "Check fan airflow and vents, or run the guided inspection below to check processor temperatures and close heat-generating tasks.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Performance",
                    Description = "Inspect processor utilization and thermal load."
                }
            },
            "network-problem" => new AutomaticDiagnosisResult
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
            "storage-problem" => new AutomaticDiagnosisResult
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
        bool hasDeviceErrors,
        double browserMemoryMb,
        int browserProcs)
    {
        var activeScopeKeys = ResolveScopeKeys(mode, scenarioId, selectedComponents);
        bool Include(params string[] keys) =>
            activeScopeKeys == null || keys.Any(activeScopeKeys.Contains);

        var proof = new List<AutomaticDiagnosisProof>();

        if (Include("cpu", "thermal", "os"))
        {
            var tempText = cpuTemp.HasValue ? $", Temp: {cpuTemp.Value:0.#}°C" : string.Empty;
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Processor (CPU)",
                Value = $"{cpuUsage:0.#}% load{tempText}",
                Status = cpuUsage >= 85 || hw.Cpu.IsThermallyThrottling ? "high" : cpuUsage >= 65 ? "elevated" : "normal",
                Meaning = $"{hw.Cpu.Name} ({hw.Cpu.Cores}C/{hw.Cpu.Threads}T @ {hw.Cpu.FrequencyMhz} MHz)."
            });
        }

        if (Include("memory", "os"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Physical Memory (RAM)",
                Value = $"{ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB)",
                Status = ramUsage >= 85 ? "high" : ramUsage >= 70 ? "elevated" : "normal",
                Meaning = browserProcs > 0
                    ? $"Active browser workload: {browserMemoryMb:0.#} MB across {browserProcs} processes."
                    : "Physical RAM utilization across active Windows processes."
            });
        }

        if (Include("storage"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Storage & S.M.A.R.T.",
                Value = $"{maxDiskUsage:0.#}% used ({(smartFailing ? "S.M.A.R.T. Warning" : "S.M.A.R.T. Healthy")})",
                Status = smartFailing || maxDiskUsage >= 90 ? "high" : maxDiskUsage >= 80 ? "elevated" : "normal",
                Meaning = $"Primary storage type: {hw.PrimaryStorageType} across {hw.StorageDrives.Count} drive(s)."
            });
        }

        if (Include("gpu", "display", "drivers"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Graphics & Drivers",
                Value = hasDeviceErrors ? $"{hw.DeviceErrors.Count} PnP Device Error(s)" : $"{hw.Gpu.Name} (OK)",
                Status = hasDeviceErrors ? "high" : "normal",
                Meaning = $"Driver {hw.Gpu.Driver}, {hw.ConnectedDisplays} display(s) connected."
            });
        }

        if (Include("network") && hw.Network != null)
        {
            var pingText = hw.Network.PingLatencyMs.HasValue ? $"{hw.Network.PingLatencyMs.Value} ms ping" : "No ping";
            var lossText = $"{hw.Network.PacketLossPercent ?? 0:0.#}% loss";
            var badNet = !hw.Network.HasActiveAdapter || (hw.Network.PacketLossPercent ?? 0) >= 5 || (hw.Network.PingLatencyMs ?? 0) >= 150;
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Network Connectivity",
                Value = hw.Network.HasActiveAdapter ? $"{pingText}, {lossText}" : "Adapter Offline",
                Status = badNet ? "elevated" : "normal",
                Meaning = hw.Network.IsWifi
                    ? $"Wi-Fi connection (Signal: {hw.Network.WifiSignalStrength}%, IP: {hw.Network.IpAddress})."
                    : $"Wired Ethernet connection (IP: {hw.Network.IpAddress})."
            });
        }

        if (Include("battery") && hw.Battery?.HasBattery == true)
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "Battery & Power State",
                Value = $"{hw.Battery.EstimatedChargeRemaining}% ({hw.Battery.StatusDescription})",
                Status = hw.Battery.EstimatedChargeRemaining <= 15 && !hw.Battery.IsCharging ? "elevated" : "normal",
                Meaning = $"Active Power Plan: {hw.ActivePowerPlan}, Health: {hw.Battery.HealthStatus}."
            });
        }
        else if (Include("os"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "OS & Power State",
                Value = hw.Battery?.HasBattery == true
                    ? $"Battery {hw.Battery.EstimatedChargeRemaining}% ({hw.Battery.StatusDescription})"
                    : $"AC Power ({hw.ActivePowerPlan})",
                Status = "normal",
                Meaning = $"{hw.OsVersion} on {hw.DeviceName} ({hw.DeviceType})."
            });
        }

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
                "network-problem" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "network" },
                "storage-problem" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "storage" },
                "slow-boot" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "storage", "memory", "os" },
                "app-crashes" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "memory", "os" },
                "blue-screen-crash" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "memory", "gpu", "drivers", "os" },
                "slow-system" or "stuttering-freezing" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cpu", "memory", "storage" },
                _ => null
            };
        }

        return null;
    }
}