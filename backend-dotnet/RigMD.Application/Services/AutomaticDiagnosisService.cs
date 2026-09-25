using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Models;

namespace RigMD.Application.Services;

/// <summary>
/// Live-telemetry intake evaluator supporting full, component, and scenario modes.
/// Builds grounded telemetry proof items and maps findings to canonical categories
/// that feed directly into the multi-turn ReAct Diagnostic & Remediation Agent.
/// </summary>
public sealed class AutomaticDiagnosisService : IAutomaticDiagnosisService
{
    public AutomaticDiagnosisResult Diagnose(AutomaticDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Hardware);

        var hw = input.Hardware;
        var mode = (input.Mode ?? "full").Trim().ToLowerInvariant();
        var scenarioId = (input.ScenarioId ?? string.Empty).Trim().ToLowerInvariant();
        var selectedComponents = (input.ComponentIds ?? Array.Empty<string>())
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        var proof = BuildTelemetryProof(hw, mode, selectedComponents, cpuUsage, cpuTemp, ramUsage, maxDiskUsage, smartFailing, hasDeviceErrors, browserMemoryMb, browserProcs);

        // 1. Hardware health critical checks (SMART failure / PnP device error codes)
        if (EvalComponent("storage") && smartFailing)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Storage health behavior",
                ActionCategory = "Escalate",
                ConfidenceLabel = "High",
                Explanation = "Live S.M.A.R.T. telemetry indicates a storage drive health warning. Back up important files immediately before running disk repairs.",
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
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Driver conflict",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = $"Windows PnP Device Manager reported hardware error code {firstErr.ErrorCode} on '{firstErr.Name}'.",
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

        // 2. Combined severe system thrashing
        if (cpuUsage >= 85 && ramUsage >= 85 && maxDiskUsage >= 85)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Severe System Resource Exhaustion",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "High",
                Explanation = $"Combined resource exhaustion detected across CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%).",
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
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = $"Thermal pressure detected on {hw.Cpu.Name} ({(cpuTemp.HasValue ? $"{cpuTemp.Value:0.#}°C" : "thermal throttling active")}).",
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
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Elevated CPU Utilization",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = cpuUsage >= 90 ? "High" : "Medium",
                Explanation = $"Live telemetry shows elevated processor load ({cpuUsage:0.#}%).",
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

            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = category,
                ActionCategory = ramUsage >= 85 ? "Troubleshoot" : "Maintain",
                ConfidenceLabel = ramUsage >= 85 ? "High" : "Medium",
                Explanation = $"Live telemetry shows {ramUsage:0.#}% RAM utilization ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB) with {browserMemoryMb:0.#} MB across {browserProcs} browser processes.",
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
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = maxDiskUsage >= 90 ? "Low Available Storage Space" : "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 90 ? "High" : "Medium",
                Explanation = $"Live telemetry shows primary storage volume utilization at {maxDiskUsage:0.#}%.",
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
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Live network telemetry detected connectivity degradation (Ping: {hw.Network.PingLatencyMs?.ToString() ?? "N/A"} ms, Packet Loss: {hw.Network.PacketLossPercent ?? 0:0.#}%).",
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
            return BuildScenarioDiagnosis(scenarioId, hw, cpuUsage, ramUsage, maxDiskUsage, proof);
        }

        // 8. Component-driven investigation when user explicitly selected components to check
        if (mode == "component" && selectedComponents.Count > 0)
        {
            return BuildComponentBaselineDiagnosis(selectedComponents, hw, cpuUsage, ramUsage, maxDiskUsage, proof);
        }

        return new AutomaticDiagnosisResult
        {
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = $"Live hardware telemetry shows CPU ({cpuUsage:0.#}%), RAM ({ramUsage:0.#}%), and Storage ({maxDiskUsage:0.#}%) operating within normal ranges. You can still launch the Autonomous ReAct Agent below for a deeper multi-tool inspection or proactive maintenance.",
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

    private static AutomaticDiagnosisResult BuildScenarioDiagnosis(
        string scenarioId,
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
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Maintain",
                ConfidenceLabel = ramUsage >= 60 || cpuUsage >= 50 ? "High" : "Medium",
                Explanation = $"Scenario check for '{scenarioId}' captured CPU at {cpuUsage:0.#}%, RAM at {ramUsage:0.#}% ({hw.Ram.UsedGb:0.0} GB used), and {hw.ProcessInsights?.BrowserMemoryMb ?? 0:0.#} MB in browser processes.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect memory-heavy processes, Windows Event Logs, and temporary cache buildup.",
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
                DiagnosedCategory = "Boot and startup failure",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Startup performance check captured storage usage at {maxDiskUsage:0.#}% on {hw.PrimaryStorageType} and RAM usage at {ramUsage:0.#}%.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent to clear temporary startup caches and inspect Windows Event Logs for boot delays.",
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
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Crash & stability scenario selected. Live telemetry captured RAM at {ramUsage:0.#}% and CPU at {cpuUsage:0.#}%; deeper Windows Event Log and system file inspection is recommended.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to query Windows System/Application Event Logs and preview a System File Checker (SFC) or cache remediation.",
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
                DiagnosedCategory = "Thermal condition",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Thermal scenario check recorded CPU load at {cpuUsage:0.#}% ({(hw.Cpu.TemperatureCelsius.HasValue ? $"{hw.Cpu.TemperatureCelsius.Value:0.#}°C" : "sensor nominal")}) under power plan '{hw.ActivePowerPlan}'.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect CPU thermals and close background processes driving heat buildup.",
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
                DiagnosedCategory = "Network issue",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Network scenario check recorded adapter ping latency at {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms and packet loss at {hw.Network?.PacketLossPercent ?? 0:0.#}%.",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect network connectivity and flush the Windows DNS cache.",
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
                DiagnosedCategory = "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = "Medium",
                Explanation = $"Storage scenario check recorded primary volume usage at {maxDiskUsage:0.#}% across {hw.StorageDrives.Count} drive(s).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect drive S.M.A.R.T. health and preview temporary/cache file cleanup.",
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

    private static AutomaticDiagnosisResult BuildComponentBaselineDiagnosis(
        HashSet<string> selectedComponents,
        HardwareProfileDto hw,
        double cpuUsage,
        double ramUsage,
        double maxDiskUsage,
        List<AutomaticDiagnosisProof> proof)
    {
        if (selectedComponents.Contains("network"))
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Network issue",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = $"Targeted network check completed (Ping: {hw.Network?.PingLatencyMs?.ToString() ?? "N/A"} ms, Packet Loss: {hw.Network?.PacketLossPercent ?? 0:0.#}%).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect adapter telemetry or flush the DNS resolver cache.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "reliability_monitor",
                    Label = "Network Diagnostics",
                    Description = "Inspect network adapter connectivity."
                }
            };
        }

        if (selectedComponents.Contains("storage"))
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Elevated Storage Utilization",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = $"Targeted storage check completed ({maxDiskUsage:0.#}% used on primary volume, S.M.A.R.T. status: {(hw.StorageDrives.Any(d => d.IsFailingSmart) ? "Warning" : "Healthy")}).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to scan temporary directories and browser caches for safe cleanup.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "storage_settings",
                    Label = "Windows Storage Settings",
                    Description = "Inspect storage volumes and temporary files."
                }
            };
        }

        if (selectedComponents.Contains("memory"))
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Elevated Memory Usage",
                ActionCategory = "Maintain",
                ConfidenceLabel = "High",
                Explanation = $"Targeted memory check recorded {ramUsage:0.#}% RAM utilization ({hw.Ram.UsedGb:0.0} / {hw.Ram.TotalGb:0.0} GB).",
                RecommendedNextStep = "Run the Autonomous ReAct Agent below to inspect top memory-consuming processes and browser workloads.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager - Memory",
                    Description = "Inspect per-process working set memory."
                }
            };
        }

        var scopeSummary = string.Join(", ", selectedComponents.Select(c => c.ToUpperInvariant()));
        return new AutomaticDiagnosisResult
        {
            DiagnosedCategory = "OS performance degradation",
            ActionCategory = "Maintain",
            ConfidenceLabel = "High",
            Explanation = $"Targeted component scan ({scopeSummary}) completed: CPU {cpuUsage:0.#}%, RAM {ramUsage:0.#}%, Storage {maxDiskUsage:0.#}%.",
            RecommendedNextStep = "Run the Autonomous ReAct Agent below to execute deep read-only diagnostic tools and preview safe maintenance actions.",
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = "task_manager",
                Label = "Task Manager",
                Description = "Inspect selected hardware subsystems."
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

        if (Include("battery", "os"))
        {
            proof.Add(new AutomaticDiagnosisProof
            {
                Label = "OS & Power State",
                Value = hw.Battery != null
                    ? $"Battery {hw.Battery.ChargePercent}% ({(hw.Battery.IsCharging ? "Charging" : "Discharging")})"
                    : $"AC Power ({hw.ActivePowerPlan})",
                Status = "normal",
                Meaning = $"{hw.OsVersion} — Power Plan: {hw.ActivePowerPlan}."
            });
        }

        return proof;
    }
}