using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Application.Services;

// A successful query is not a healthy component. Verdicts must be supported by the scoped evidence.
internal static class ScannerEvidencePolicy
{
    internal static readonly string[] Components =
        ["cpu", "memory", "gpu", "storage", "os", "startup", "drivers", "thermal", "battery", "network", "display", "peripherals", "audio"];
    internal static readonly string[] Scenarios =
        ["slow-system", "slow-boot", "blue-screen-crash", "driver-error", "no-display", "overheating-loud-fan", "network-problem", "app-crashes", "stuttering-freezing", "storage-problem", "rapid-battery-drain"];

    public static AutomaticDiagnosisResult Complete(AutomaticDiagnosisInput input, AutomaticDiagnosisResult result)
    {
        var mode = (input.Mode ?? "").Trim().ToLowerInvariant();
        var selected = (input.ComponentIds ?? Array.Empty<string>()).Select(DiagnosticScopeMapper.NormalizeComponentId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scenario = DiagnosticScopeMapper.NormalizeScenarioId(input.ScenarioId);
        var proof = result.Proof.ToList();
        bool Includes(string id) => mode == "full" || mode == "component" && selected.Contains(id);
        void Missing(string id, string label, string reason) => proof.Add(new AutomaticDiagnosisProof
        {
            MetricId = id, Label = label, Value = "Not verified", Status = "unknown", Meaning = reason,
            CapturedAt = input.CapturedAt == default ? null : input.CapturedAt
        });

        if (mode is not ("full" or "component" or "scenario") ||
            mode == "component" && (selected.Count == 0 || selected.Any(id => !Components.Contains(id))) ||
            mode == "scenario" && !Scenarios.Contains(scenario))
        {
            proof.Clear();
            Missing("scanner_scope", "Selected check", "Select a supported component or problem check. No diagnosis was inferred from an unknown selection.");
        }
        else
        {
            if ((Includes("gpu") || Includes("display") || scenario == "no-display") &&
                (!input.Hardware.Gpu.HasGpu || string.IsNullOrWhiteSpace(input.Hardware.Gpu.Name) || input.Hardware.Gpu.Name == "Unknown GPU"))
            {
                proof.RemoveAll(p => p.Label == "GPU / Graphics" && p.Value == "Not Present");
                Missing("graphics_detection", "Graphics adapter detection", "Graphics metadata was unavailable. This does not establish that the adapter is physically absent.");
            }
            if (result.ComponentStatus == ComponentStatus.NotPresent &&
                (selected.SetEquals(["battery"]) || scenario == "rapid-battery-drain") && input.Hardware.Battery?.QuerySucceeded == true)
            {
                foreach (var reading in proof)
                {
                    reading.EvidenceKind = "reported_status";
                    reading.RuleVersion = ReadingEvidence.RuleVersion;
                }
                return new AutomaticDiagnosisResult
                {
                    ComponentStatus = result.ComponentStatus, TargetScope = result.TargetScope,
                    PrimaryResult = result.PrimaryResult, DiagnosedCategory = result.DiagnosedCategory,
                    ActionCategory = result.ActionCategory, ConfidenceLabel = "NotApplicable",
                    Explanation = result.Explanation, RecommendedNextStep = result.RecommendedNextStep,
                    IncidentalWarning = result.IncidentalWarning, Proof = result.Proof,
                    VerificationTarget = result.VerificationTarget
                };
            }
            if (Includes("startup") || scenario == "slow-boot")
                Missing("startup_impact", "Startup duration and app impact", "Startup entries and current workload do not measure boot duration or individual startup impact. Review Task Manager Startup apps and boot records.");
            if ((Includes("battery") || scenario == "rapid-battery-drain") && input.Hardware.Battery?.HasBattery == true)
                Missing("battery_drain", "Battery wear and drain rate", "Charge is not capacity or drain rate. This check has no timed discharge measurement or full-charge/design-capacity comparison. Review a Windows battery report.");
            if (Includes("os"))
                Missing("os_integrity", "Windows file integrity", "Windows version and power plan do not verify system files. No integrity test was performed during this scan.");
            if (Includes("audio"))
                Missing("audio_playback", "Sound playback and recording", "Device status does not test volume, output selection, speakers or microphone. Use Windows Sound troubleshooting and a playback/recording test.");
            if (Includes("peripherals"))
                Missing("peripheral_function", "USB and peripheral function", "Device status does not test cables, ports, power delivery or input. Test the affected device and connection separately.");
            if (Includes("display") || scenario == "no-display")
                Missing("display_picture", "Picture and display connection", "A detected monitor does not confirm the visible picture, cable or panel. Check the affected screen, connection and display settings.");
            if (Includes("thermal") || scenario == "overheating-loud-fan")
                Missing("fan_operation", "Fan operation and airflow", "This scan does not measure fan speed, airflow or noise. A temperature snapshot cannot verify cooling under sustained load.");
        }

        foreach (var reading in proof)
        {
            reading.EvidenceKind = ReadingEvidence.Kind(reading.MetricId, reading.Status);
            reading.RuleVersion = ReadingEvidence.RuleVersion;
        }
        var warnings = proof.Where(p => p.Status is "high" or "elevated" or "still needs attention").ToArray();
        var missing = proof.Where(p => p.Status == "unknown").ToArray();
        var active = warnings.Length > 0;
        var category = active ? Category(warnings, result.DiagnosedCategory, scenario) : missing.Length > 0
            ? result.DiagnosedCategory.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                ? result.DiagnosedCategory : "Scanner evidence unavailable"
            : "No Active Issue Detected";
        var summary = active
            ? "Readings needing attention: " + string.Join("; ", warnings.Select(p => $"{p.Label}: {p.Value}")) + "."
            : missing.Length > 0 ? "No warning was confirmed in the available readings, but this check is incomplete."
            : "No warning was found in the readings checked at this time.";
        if (missing.Length > 0) summary += " Not verified: " + string.Join(", ", missing.Select(p => p.Label)) + ".";
        var limits = "Evidence availability describes what this scan collected, not confidence in a cause or hardware health. Usage thresholds are RigMD review policies, not proof of a fault, memory leak or paging. Temperature limits vary by device; this scan does not establish the manufacturer's thermal limit. Recorded errors do not establish their cause or whether a fault is happening now.";
        var next = !active ? missing.Length > 0
            ? "Review the items not verified and run the relevant Windows or manufacturer check if symptoms continue."
            : "No repair is indicated by these readings. Recheck if symptoms return."
            : category switch
            {
                "Storage health behavior" => RepairSupportPolicy.HardwareNotice,
                "High Memory Pressure" => "Save your work, review the apps using memory and close only apps you no longer need. File cleanup frees storage, not app memory. This is not a memory-cell test.",
                "Elevated CPU Utilization" => "Review active processes in Task Manager and recheck over time. High activity can be normal for the workload; it does not establish processor damage.",
                "Thermal condition" => "Review the device maker's temperature limits, vents and cooling guidance. RigMD cannot repair a physical cooling fault; seek a hardware inspection if overheating symptoms persist.",
                "Device status warning" => "Review each affected device and its exact Device Manager code. Follow the code-specific Windows or device-maker guidance; this does not by itself identify a driver conflict.",
                "Elevated Storage Utilization" => "Review free space on the affected volumes and preview optional cleanup. Capacity use is not disk activity, a boot-time measurement or proof of drive failure.",
                "Network issue" => "Review adapter, address, route and address-lookup results separately. Check the affected service too; a failed ICMP probe alone does not confirm an internet outage.",
                "Low battery charge" => "Connect a suitable power supply if needed. To investigate wear or rapid drain, review a Windows battery report and measurements over time; charge alone cannot establish either.",
                "Application crash history requires review" or "System crash or stop error requires review" => "Review matching records, times, affected apps and stop codes in Reliability Monitor. A historical record is not a confirmed cause or an ongoing fault; an unchanged old event cannot verify a repair.",
                _ => "Review each highlighted reading before choosing an action."
            };
        if (warnings.Length > 1) next += " Review the other highlighted readings too; an action for one warning may not address the others.";
        return new AutomaticDiagnosisResult
        {
            ComponentStatus = ComponentStatus.Present, TargetScope = result.TargetScope,
            PrimaryResult = summary, DiagnosedCategory = category, ActionCategory = active
                ? category.Contains("Storage health", StringComparison.OrdinalIgnoreCase) ? "Escalate" : "Troubleshoot" : "Monitor",
            ConfidenceLabel = ReadingEvidence.Coverage(proof),
            Explanation = summary + " " + limits, RecommendedNextStep = next,
            IncidentalWarning = result.IncidentalWarning, Proof = proof, VerificationTarget = result.VerificationTarget
        };
    }

    private static string Category(AutomaticDiagnosisProof[] warnings, string original, string scenario)
    {
        bool Has(string id) => warnings.Any(p => p.MetricId == id);
        if (Has("storage_health")) return "Storage health behavior";
        if (warnings.Any(p => p.MetricId.StartsWith("device_error_"))) return "Device status warning";
        if (Has("cpu_temperature") || Has("gpu_temperature") || Has("cpu_throttling")) return "Thermal condition";
        if (Has("application_crash_history")) return "Application crash history requires review";
        if (Has("system_crash_history")) return "System crash or stop error requires review";
        if (scenario == "app-crashes") return "Application stability risk needs review";
        if (scenario == "blue-screen-crash") return "System crash or stop error requires review";
        if (Has("memory_usage") || Has("memory_app_warning")) return "High Memory Pressure";
        if (Has("cpu_usage")) return "Elevated CPU Utilization";
        if (Has("storage_usage")) return "Elevated Storage Utilization";
        if (warnings.Any(p => p.MetricId.StartsWith("network_"))) return "Network issue";
        if (Has("battery_charge")) return "Low battery charge";
        return original == "No Active Issue Detected" ? "Measured warning requires review" : original;
    }
}
