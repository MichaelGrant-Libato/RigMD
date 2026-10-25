using RigMD.Application.Models;

namespace RigMD.Application.Services;

// Describes collected evidence, never a probability of a hardware fault or a root cause.
internal static class ReadingEvidence
{
    public const string RuleVersion = "windows-review-v2";
    public static string Kind(string metric, string status) => status == "unknown" ? "unavailable"
        : metric is "application_crash_history" or "system_crash_history" ? "event_log"
        : metric.StartsWith("device_error_", StringComparison.Ordinal) || metric is "graphics_devices" or "storage_health" or "cpu_throttling" or "memory_app_warning" ? "reported_status"
        : metric is "cpu_usage" or "memory_usage" or "storage_usage" or "cpu_temperature" or "gpu_temperature" or "battery_charge" || metric.StartsWith("network_", StringComparison.Ordinal) ? "measurement"
        : "context";

    public static string Coverage(IReadOnlyList<AutomaticDiagnosisProof> proof)
    {
        var available = proof.Any(p => p.EvidenceKind is "measurement" or "event_log" or "reported_status");
        var missing = proof.Any(p => p.Status == "unknown");
        return missing ? available ? "Partial" : "Unavailable" : available ? "Available" : "ContextOnly";
    }
}
