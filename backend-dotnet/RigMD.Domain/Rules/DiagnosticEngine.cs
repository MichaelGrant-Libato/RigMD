using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RigMD.Domain.Rules;

public class DiagnosticSymptomPayload
{
    [JsonPropertyName("symptom_type")]
    public string SymptomType { get; set; } = string.Empty;

    [JsonPropertyName("warning_signs")]
    public string WarningSigns { get; set; } = string.Empty;

    [JsonPropertyName("warning_signs_label")]
    public string WarningSignsLabel { get; set; } = string.Empty;

    [JsonPropertyName("recent_changes")]
    public string RecentChanges { get; set; } = string.Empty;

    [JsonPropertyName("recent_changes_label")]
    public string RecentChangesLabel { get; set; } = string.Empty;

    [JsonPropertyName("frequency")]
    public string Frequency { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("affected_activity")]
    public string AffectedActivity { get; set; } = string.Empty;

    [JsonPropertyName("affected_activity_label")]
    public string AffectedActivityLabel { get; set; } = string.Empty;

    [JsonPropertyName("system_state")]
    public string SystemState { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = "N/A";

    [JsonPropertyName("mentions_black_screen")]
    public bool MentionsBlackScreen { get; set; }

    [JsonPropertyName("mentions_no_boot")]
    public bool MentionsNoBoot { get; set; }

    [JsonPropertyName("mentions_blue_screen")]
    public bool MentionsBlueScreen { get; set; }

    [JsonPropertyName("mentions_storage")]
    public bool MentionsStorage { get; set; }

    [JsonPropertyName("mentions_heat")]
    public bool MentionsHeat { get; set; }

    [JsonPropertyName("mentions_flicker")]
    public bool MentionsFlicker { get; set; }

    [JsonPropertyName("mentions_slow")]
    public bool MentionsSlow { get; set; }
}

public class HardwareMetrics
{
    public double CpuUsagePercent { get; set; }
    public double RamUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
    public string StorageType { get; set; } = string.Empty;
    public double BrowserMemoryMb { get; set; }
    public int BrowserProcessCount { get; set; }
    public bool BrowserHeavy { get; set; }
    public bool GameDetected { get; set; }
    public List<string> GameProcesses { get; set; } = new();

    public bool CpuThermalThrottling { get; set; }
    public bool SmartDriveWarning { get; set; }
    public string? MemoryLeakWarning { get; set; }
}

public class DiagnosticResult
{
    public string DiagnosedCategory { get; set; } = string.Empty;
    public string ActionCategory { get; set; } = string.Empty;
    public string ConfidenceLabel { get; set; } = string.Empty;
    public string AiExplanation { get; set; } = string.Empty;
    public string RecommendedNextStep { get; set; } = string.Empty;

    public List<DiagnosticProofItem> Proof { get; set; } = new();
    public List<DiagnosticEvidenceItem> Evidence { get; set; } = new();
    public List<DiagnosticProofItem> AllLiveProof { get; set; } = new();
    public VerificationTarget Target { get; set; } = new();
}

public class DiagnosticProofItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
}

public class DiagnosticEvidenceItem
{
    public string Category { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Points { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class VerificationTarget
{
    public string Target { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Phase 1 Reset: Replaces the 1,739-line hardcoded point-scoring script with a
/// lightweight baseline telemetry snapshot mapper. Real diagnosis reasoning will
/// be performed by the LLM tool-calling agent in Phase 2/3.
/// </summary>
public static class DiagnosticEngine
{
    public static DiagnosticResult RunDiagnostic(
        DiagnosticSymptomPayload payload,
        HardwareMetrics? liveMetrics = null)
    {
        var metrics = liveMetrics ?? new HardwareMetrics();

        var proof = new List<DiagnosticProofItem>
        {
            new()
            {
                Label = "CPU load",
                Value = $"{metrics.CpuUsagePercent:0.#}%",
                Status = metrics.CpuUsagePercent >= 85 ? "high" : metrics.CpuUsagePercent >= 65 ? "elevated" : "normal",
                Meaning = "Current processor utilization."
            },
            new()
            {
                Label = "RAM usage",
                Value = $"{metrics.RamUsagePercent:0.#}%",
                Status = metrics.RamUsagePercent >= 85 ? "high" : metrics.RamUsagePercent >= 70 ? "elevated" : "normal",
                Meaning = "Current physical memory utilization."
            },
            new()
            {
                Label = "Storage usage",
                Value = $"{metrics.DiskUsagePercent:0.#}%",
                Status = metrics.DiskUsagePercent >= 90 ? "high" : metrics.DiskUsagePercent >= 80 ? "elevated" : "normal",
                Meaning = "Primary drive space utilization."
            }
        };

        var category = "OS performance degradation";
        var actionCategory = "Troubleshoot";
        var target = new VerificationTarget
        {
            Target = "task_manager",
            Label = "Task Manager",
            Description = "Inspect active CPU and memory usage."
        };

        if (metrics.DiskUsagePercent >= 85 || payload.MentionsStorage)
        {
            category = "Storage health behavior";
            actionCategory = "Maintain";
            target = new VerificationTarget
            {
                Target = "storage_settings",
                Label = "Storage Settings",
                Description = "Review disk usage and temporary files."
            };
        }
        else if (metrics.CpuThermalThrottling || payload.MentionsHeat)
        {
            category = "Thermal condition";
            actionCategory = "Troubleshoot";
        }
        else if (metrics.CpuUsagePercent < 65 && metrics.RamUsagePercent < 75 && string.IsNullOrWhiteSpace(payload.SymptomType))
        {
            category = "No active issue detected";
            actionCategory = "Monitor";
        }

        return new DiagnosticResult
        {
            DiagnosedCategory = category,
            ActionCategory = actionCategory,
            ConfidenceLabel = "Medium",
            RecommendedNextStep = "Review live telemetry and run safe remediation if needed.",
            Proof = proof,
            AllLiveProof = proof,
            Evidence = new List<DiagnosticEvidenceItem>(),
            Target = target
        };
    }
}