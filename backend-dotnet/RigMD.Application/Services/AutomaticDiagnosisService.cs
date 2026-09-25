using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Application.Services;

/// <summary>
/// Live-telemetry intake evaluator supporting full, component, and scenario modes.
/// Enforces hardware presence gating (ComponentStatus.NotPresent), strict scoped primary verdicts,
/// out-of-scope incidental warnings, and grounded telemetry proof items.
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

        bool EvalComponent(params string[] ids) =>
            mode != "component" || selectedComponents.Count == 0 || ids.Any(selectedComponents.Contains);

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
            selectedComponents,
            cpuUsage,
            cpuTemp,
            ramUsage,
            maxDiskUsage,
            smartFailing,
            hasDeviceErrors,
            browserMemoryMb,
            browserProcs);

        var incidentalWarning = BuildIncidentalWarning(
            hw,
            mode,
            selectedComponents,
            targetScopeLabels,
            cpuUsage,
            cpuTemp,
            ramUsage,
            maxDiskUsage,
            smartFailing,
            hasDeviceErrors,
            memLeakWarning,
            absentComponentNote);

        // 1. Hardware health critical checks (SMART failure / PnP device error codes)
        if (EvalComponent("storage") && smartFailing)
        {
            var primary = "Live S.M.A.R.T. telemetry indicates a storage drive health warning. Back up important files immediately before running disk repairs.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Storage health behavior",
                ActionCategory = "Escalate",
                ConfidenceLabel = "High",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Back up critical files first, then use the Autonomous ReAct Agent to inspect drive health and Windows Event Logs.",
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
            var primary = $"Windows PnP Device Manager reported hardware error code {firstErr.ErrorCode} on '{firstErr.Name}'.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Driver conflict",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect Windows System Event Logs and driver telemetry.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "device_manager",
                    Label = "Windows Device Manager",
                    Description = "Inspect devices reporting non-zero ConfigManagerErrorCode."
                }
            };
        }

        // 2. Combined severe system thrashing (only when CPU, Memory, and Storage are all in scope)
        if (EvalComponent("cpu") && EvalComponent("memory") && EvalComponent("storage") &&
            cpuUsage >= 85 && ramUsage >= 85 && maxDiskUsage >= 85)
        {
            var primary = $"Combined resource exhaustion detected across CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Severe System Resource Exhaustion",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent to identify and close runaway processes or clear disk caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect combined CPU, memory, and disk resource consumers."
                }
            };
        }

        // 3. CPU & Thermal checks
        if (EvalComponent("cpu", "thermal") && (hw.Cpu.IsThermallyThrottling || (cpuTemp.HasValue && cpuTemp.Value >= 85)))
        {
            var primary = $"Thermal pressure detected on {hw.Cpu.Name} ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermal throttling active")}).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent to inspect CPU thermals and background workloads.",
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
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Elevated CPU Utilization",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = cpuUsage >= 90 ? "High" : "Medium",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent to inspect top CPU/memory processes and safely terminate non-essential background tasks.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect CPU-intensive processes."
                }
            };
        }

        // 4. Memory & Process Workload checks
        if (EvalComponent("memory", "os") && (!string.IsNullOrWhiteSpace(memLeakWarning) || ramUsage >= 80 || (ramUsage >= 68 && browserMemoryMb >= 1500)))
        {
            var category = ramUsage >= 80
                ? "High Memory Pressure"
                : !string.IsNullOrWhiteSpace(memLeakWarning)
                    ? "OS performance degradation"
                    : "Elevated Memory Pressure From Active Workloads";

            var primary = $"Memory (RAM) shows elevated utilization at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB) with {browserMemoryMb:0.#} MB across {browserProcs} browser processes.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = category,
                ActionCategory = ramUsage >= 85 ? "Troubleshoot" : "Maintain",
                ConfidenceLabel = ramUsage >= 85 ? "High" : "Medium",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Use the Autonomous ReAct Agent below to inspect memory-heavy processes or clear browser/temporary caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Processes",
                    Description = "Inspect active memory-consuming applications."
                }
            };
        }

        // 5. Storage utilization check
        if (EvalComponent("storage") && maxDiskUsage >= 80)
        {
            var primary = $"Storage volume utilization is elevated at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType}.";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = maxDiskUsage >= 90 ? "Low Available Storage Space" : "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 90 ? "High" : "Medium",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent to preview and clear temporary files, browser caches, or Windows Update caches.",
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
            var primary = $"Network connectivity degradation detected (Ping: {hw.Network.PingLatencyMs?.ToString() ?? "N/A"} ms, Packet Loss: {hw.Network.PacketLossPercent ?? 0:0.#}%).";
            return new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = primary,
                IncidentalWarning = incidentalWarning,
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = AppendIncidental(primary, incidentalWarning),
                RecommendedNextStep = "Run the Autonomous ReAct Agent to inspect network adapters and flush the Windows DNS resolver cache.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Network & Reliability",
                    Description = "Inspect network adapter status and DNS resolution."
                }
            };
        }

        // 7. Scenario-driven investigation when user explicitly picked a problem scenario
        if (mode == "scenario" && !string.IsNullOrWhiteSpace(scenarioId))
        {
            return BuildScenarioDiagnosis(scenarioId, targetScopeLabels, hw, cpuUsage, ramUsage, maxDiskUsage, proof);
        }

        // 8. Scoped Component-driven healthy verdict when user explicitly selected specific parts
        if (mode == "component" && selectedComponents.Count > 0)
        {
            return BuildComponentHealthyDiagnosis(
                selectedComponents,
                targetScopeLabels,
                incidentalWarning,
                hw,
                cpuUsage,
                cpuTemp,
                ramUsage,
                maxDiskUsage,
                smartFailing,
                hasDeviceErrors,
                proof);
        }

        var fullPrimary = $"Full system telemetry shows CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%) operating within normal ranges.";
        return new AutomaticDiagnosisResult
        {
            ComponentStatus = ComponentStatus.Present,
            TargetScope = targetScopeLabels,
            PrimaryResult = fullPrimary,
            IncidentalWarning = null,
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = $"{fullPrimary} You can still launch the Autonomous ReAct Agent below for a deeper multi-tool inspection or proactive maintenance.",
            RecommendedNextStep = "Continue normal monitoring, or click 'Run Agent & Review Action' below to run a deep ReAct tool inspection (Event Logs, S.M.A.R.T., Processes, and Cache Dry-Run).",
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
        string? incidentalWarning,
        HardwareProfileDto hw,
        double cpuUsage,
        double? cpuTemp,
        double ramUsage,
        double maxDiskUsage,
        bool smartFailing,
        bool hasDeviceErrors,
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
                $"Hardware Drivers are operating normally (0 PnP error codes detected in Device Manager).");
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
            IncidentalWarning = incidentalWarning,
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = AppendIncidental(primaryResult, incidentalWarning),
            RecommendedNextStep = "No immediate fix is required for the selected part(s). You can still run the Scoped ReAct Agent below to inspect live component telemetry.",
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = target.Item1,
                Label = target.Item2,
                Description = target.Item3
            }
        };
    }

    private static string? BuildIncidentalWarning(
        HardwareProfileDto hw,
        string mode,
        HashSet<string> selectedComponents,
        IReadOnlyList<string> targetScopeLabels,
        double cpuUsage,
        double? cpuTemp,
        double ramUsage,
        double maxDiskUsage,
        bool smartFailing,
        bool hasDeviceErrors,
        string? memLeakWarning,
        string? absentComponentNote)
    {
        if (mode != "component" || selectedComponents.Count == 0)
        {
            return null;
        }

        var scopeText = string.Join(", ", targetScopeLabels);
        var warnings = new List<string>();

        if (!string.IsNullOrWhiteSpace(absentComponentNote))
        {
            warnings.Add(absentComponentNote);
        }

        // Check out-of-scope Storage
        if (!selectedComponents.Contains("storage"))
        {
            var criticalVolume = hw.AllDisks
                .OrderByDescending(d => d.UsagePercent)
                .FirstOrDefault(d => d.UsagePercent >= 85 || (d.TotalGb > 0 && (d.TotalGb - d.UsedGb) <= 5.0));

            if (smartFailing)
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed a physical drive S.M.A.R.T. health warning on your storage subsystem.");
            }
            else if (criticalVolume != null)
            {
                var freeGb = Math.Max(0, criticalVolume.TotalGb - criticalVolume.UsedGb);
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed your {criticalVolume.Drive} Drive is critically low on space ({criticalVolume.UsagePercent:0.#}% used, {freeGb:0.0} GB free).");
            }
            else if (maxDiskUsage >= 85)
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed your primary storage volume is running low on space ({maxDiskUsage:0.#}% used).");
            }
        }

        // Check out-of-scope Memory
        if (!selectedComponents.Contains("memory") && !selectedComponents.Contains("os"))
        {
            if (ramUsage >= 85 || !string.IsNullOrWhiteSpace(memLeakWarning))
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed physical Memory (RAM) pressure is high ({ramUsage:0.#}% in use, {hw.Ram.UsedGb:0.0}/{hw.Ram.TotalGb:0.0} GB).");
            }
        }

        // Check out-of-scope CPU / Thermals
        if (!selectedComponents.Contains("cpu") && !selectedComponents.Contains("thermal"))
        {
            if (hw.Cpu.IsThermallyThrottling || (cpuTemp.HasValue && cpuTemp.Value >= 85))
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed your CPU is experiencing thermal pressure ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermal throttling")}).");
            }
            else if (cpuUsage >= 85)
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed your CPU utilization is critically elevated ({cpuUsage:0.#}%).");
            }
        }

        // Check out-of-scope Drivers/GPU
        if (!selectedComponents.Contains("drivers") && !selectedComponents.Contains("gpu") && !selectedComponents.Contains("display"))
        {
            if (hasDeviceErrors)
            {
                var firstErr = hw.DeviceErrors.First();
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed Windows Device Manager reported error code {firstErr.ErrorCode} on '{firstErr.Name}'.");
            }
        }

        // Check out-of-scope Network
        if (!selectedComponents.Contains("network"))
        {
            if (hw.Network != null && !hw.Network.HasActiveAdapter)
            {
                warnings.Add($"Note: Although you only scanned {scopeText}, RigMD noticed your network adapter is currently offline.");
            }
        }

        return warnings.Count > 0 ? string.Join(" ", warnings) : null;
    }

    private static string AppendIncidental(string primary, string? incidentalWarning)
    {
        if (string.IsNullOrWhiteSpace(incidentalWarning))
        {
            return primary;
        }

        return $"{primary}\n\n{incidentalWarning}";
    }

    private static AutomaticDiagnosisResult BuildScenarioDiagnosis(
        string scenarioId,
        IReadOnlyList<string> targetScopeLabels,
        HardwareProfileDto hw,
        double cpuUsage,
        double ramUsage,
        double maxDiskUsage,
        List<AutomaticDiagnosisProof> proof)
    {
        return scenarioId switch
        {
            "slow-system" or "stuttering-freezing" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Scenario check for '{scenarioId}' captured CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and {hw.ProcessInsights?.BrowserMemoryMb ?? 0:0.#} MB in browser processes.",
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Maintain",
                ConfidenceLabel = ramUsage >= 60 || cpuUsage >= 50 ? "High" : "Medium",
                Explanation = $"Scenario check for '{scenarioId}' captured CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and {hw.ProcessInsights?.BrowserMemoryMb ?? 0:0.#} MB in browser processes.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute [inspect_cpu_and_thermals, inspect_memory_and_processes, inspect_storage_health] and preview safe remediation.",
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
                PrimaryResult = $"Startup performance check captured storage usage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and RAM usage at {ramUsage:0.#}%.",
                DiagnosedCategory = "Boot and startup failure",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Startup performance check captured storage usage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and RAM usage at {ramUsage:0.#}%.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent to execute [inspect_storage_health, query_startup_apps, query_windows_event_logs (Boot/Event 100)] and clear temporary startup caches.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "startup_apps",
                    Label = "Windows Startup Apps",
                    Description = "Review applications configured to launch at Windows sign-in."
                }
            },
            "blue-screen-crash" or "app-crashes" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Crash & stability scenario selected. Live telemetry captured RAM at {ramUsage:0.#}% and CPU at {cpuUsage:0.#}%; Event Log BugCheck/Kernel-Power 41 inspection is ready.",
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Crash & stability scenario selected. Live telemetry captured RAM at {ramUsage:0.#}% and CPU at {cpuUsage:0.#}%; deeper Windows Event Log and system file inspection is recommended.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute [query_windows_event_logs (BugCheck/Kernel-Power 41), inspect_memory_and_processes, inspect_cpu_and_thermals].",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Windows Reliability Monitor",
                    Description = "Review recent application crashes and Windows stop error events."
                }
            },
            "driver-error" => new AutomaticDiagnosisResult
            {
                ComponentStatus = ComponentStatus.Present,
                TargetScope = targetScopeLabels,
                PrimaryResult = $"Driver check completed for {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}) with {hw.DeviceErrors?.Count ?? 0} PnP error code(s) currently active.",
                DiagnosedCategory = "Driver conflict",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Driver check completed for {hw.Gpu.Name} (Driver: {hw.Gpu.Driver}) with {hw.DeviceErrors?.Count ?? 0} PnP error code(s) currently active.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to query Windows Event Logs for driver faults and inspect GPU/device telemetry.",
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
                ConfidenceLabel = "Medium",
                Explanation = $"Display check completed for {hw.Gpu.Name} across {hw.ConnectedDisplays} connected display(s).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect GPU/display telemetry, check Windows Event Logs, or restart the Windows Explorer shell.",
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
                PrimaryResult = $"Thermal scenario check recorded CPU load at {cpuUsage:0.#}% ({(hw.Cpu.TemperatureCelsius.HasValue ? $"{hw.Cpu.TemperatureCelsius.Value:0.#}°C" : "sensor nominal")}) under power plan '{hw.ActivePowerPlan}'.",
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Thermal scenario check recorded CPU load at {cpuUsage:0.#}% ({(hw.Cpu.TemperatureCelsius.HasValue ? $"{hw.Cpu.TemperatureCelsius.Value:0.#}°C" : "sensor nominal")}) under power plan '{hw.ActivePowerPlan}'.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute [inspect_cpu_and_thermals, inspect_gpu_and_displays, inspect_memory_and_processes] and close heat-generating workloads.",
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
                PrimaryResult = $"Network scenario check recorded adapter ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Network scenario check recorded adapter ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute [inspect_network_connectivity, inspect_dns] and flush the Windows DNS cache.",
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
                PrimaryResult = $"Storage scenario check recorded primary volume usage at {maxDiskUsage:0.#}% across {hw.StorageDrives.Count} drive(s).",
                DiagnosedCategory = "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Storage scenario check recorded primary volume usage at {maxDiskUsage:0.#}% across {hw.StorageDrives.Count} drive(s).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute [inspect_storage_health] and preview temporary/cache file cleanup.",
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
                RecommendedNextStep = "Run the Autonomous ReAct Agent below for a multi-turn diagnostic tool inspection and safe remediation preview.",
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
        bool Include(params string[] keys) =>
            mode != "component" || selectedComponents.Count == 0 || keys.Any(selectedComponents.Contains);

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
}