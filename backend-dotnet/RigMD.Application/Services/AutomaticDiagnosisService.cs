using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Models;

namespace RigMD.Application.Services;

/// <summary>
/// Phase 1 Reset: Lightweight live-telemetry baseline evaluator replacing the
/// 1,562-line hardcoded rule script. In Phase 2/3, the LLM ReAct agent will
/// drive multi-step diagnostic tool calls directly.
/// </summary>
public sealed class AutomaticDiagnosisService : IAutomaticDiagnosisService
{
    public AutomaticDiagnosisResult Diagnose(AutomaticDiagnosisInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Hardware);

        var hw = input.Hardware;
        var cpuUsage = hw.Cpu.UsagePercent;
        var ramUsage = hw.Ram.UsagePercent;
        var maxDiskUsage = hw.AllDisks.Any()
            ? hw.AllDisks.Max(d => d.UsagePercent)
            : hw.StorageDrives.FirstOrDefault()?.UsagePercent ?? 0;

        var proof = new List<AutomaticDiagnosisProof>
        {
            new()
            {
                Label = "CPU Utilization",
                Value = $"{cpuUsage:0.#}%",
                Status = cpuUsage >= 85 ? "high" : cpuUsage >= 65 ? "elevated" : "normal",
                Meaning = $"Live processor load on {hw.Cpu.Name}."
            },
            new()
            {
                Label = "Memory Utilization",
                Value = $"{ramUsage:0.#}% ({hw.Ram.UsedGb:0.1} / {hw.Ram.TotalGb:0.1} GB)",
                Status = ramUsage >= 85 ? "high" : ramUsage >= 70 ? "elevated" : "normal",
                Meaning = "Live physical RAM consumption across running processes."
            },
            new()
            {
                Label = "Primary Storage Usage",
                Value = $"{maxDiskUsage:0.#}%",
                Status = maxDiskUsage >= 90 ? "high" : maxDiskUsage >= 80 ? "elevated" : "normal",
                Meaning = "Disk capacity usage on active volumes."
            }
        };

        if (ramUsage >= 80)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Memory Resource Pressure",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = ramUsage >= 90 ? "High" : "Medium",
                Explanation = $"Live telemetry shows elevated memory consumption ({ramUsage:0.#}% used).",
                RecommendedNextStep = "Review memory-heavy applications or run an autonomous remediation check.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect active memory-consuming processes."
                }
            };
        }

        if (maxDiskUsage >= 85)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "Storage Capacity Pressure",
                ActionCategory = "Maintain",
                ConfidenceLabel = maxDiskUsage >= 92 ? "High" : "Medium",
                Explanation = $"Live telemetry shows volume usage at {maxDiskUsage:0.#}%.",
                RecommendedNextStep = "Clear temporary files or inspect storage usage.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "storage_settings",
                    Label = "Storage Settings",
                    Description = "Inspect volume free space and temporary files."
                }
            };
        }

        if (cpuUsage >= 85 || hw.Cpu.IsThermallyThrottling)
        {
            return new AutomaticDiagnosisResult
            {
                DiagnosedCategory = "OS performance degradation",
                ActionCategory = "Troubleshoot",
                ConfidenceLabel = "Medium",
                Explanation = $"Live telemetry detected elevated CPU activity ({cpuUsage:0.#}%).",
                RecommendedNextStep = "Inspect background processes and thermal conditions.",
                Proof = proof,
                VerificationTarget = new AutomaticVerificationTarget
                {
                    Target = "task_manager",
                    Label = "Task Manager",
                    Description = "Inspect CPU-intensive processes."
                }
            };
        }

        return new AutomaticDiagnosisResult
        {
            DiagnosedCategory = "No Active Issue Detected",
            ActionCategory = "Monitor",
            ConfidenceLabel = "High",
            Explanation = "Baseline hardware telemetry is within normal operating thresholds.",
            RecommendedNextStep = "Continue normal operation or trigger a targeted check if symptoms recur.",
            Proof = proof,
            VerificationTarget = new AutomaticVerificationTarget
            {
                Target = "task_manager",
                Label = "Task Manager",
                Description = "View live Windows resource utilization."
            }
        };
    }
}