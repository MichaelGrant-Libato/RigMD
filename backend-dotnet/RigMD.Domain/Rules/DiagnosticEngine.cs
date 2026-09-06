using System;
using System.Collections.Generic;
using System.Linq;
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

public static class DiagnosticEngine
{
    private static string _Text(string? value) => (value ?? "").ToLower().Trim();

    private static bool _Has(string? value, params string[] words)
    {
        var text = _Text(value);
        return words.Any(word => text.Contains(word));
    }

    private static string _JoinedText(params string?[] values)
    {
        var valid = values.Select(_Text).Where(v => !string.IsNullOrEmpty(v));
        return string.Join(" ", valid);
    }

    private static void _Add(Dictionary<string, int> score, List<DiagnosticEvidenceItem> evidence, string category, int points, string label, string? value, string source = "User report")
    {
        if (!score.ContainsKey(category)) return;

        score[category] += points;
        evidence.Add(new DiagnosticEvidenceItem
        {
            Category = category,
            Label = label,
            Value = value ?? "",
            Points = points,
            Source = source
        });
    }

    private static DiagnosticProofItem _MakeProof(string label, string value, string status, string meaning)
    {
        return new DiagnosticProofItem
        {
            Label = label,
            Value = value,
            Status = status,
            Meaning = meaning
        };
    }

    private static string? _ReportedCategory(string symptomType)
    {
        var text = _Text(symptomType);
        if (text.Contains("os performance")) return "OS performance degradation";
        if (text.Contains("driver")) return "Driver conflict";
        if (text.Contains("storage")) return "Storage health behavior";
        if (text.Contains("boot") || text.Contains("startup")) return "Boot and startup failure";
        if (text.Contains("display") || text.Contains("rendering")) return "Display driver behavior";
        if (text.Contains("thermal")) return "Thermal condition";
        return null;
    }

    private static string _ProofStatus(List<DiagnosticProofItem> proof, string label)
    {
        var item = proof.FirstOrDefault(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));
        return item?.Status.ToLower() ?? "normal";
    }

    private static string _ProofValue(List<DiagnosticProofItem> proof, string label)
    {
        var item = proof.FirstOrDefault(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));
        return item?.Value ?? "";
    }

    private static bool _HasPressure(List<DiagnosticProofItem> proof, params string[] labels)
    {
        return labels.Any(label => 
        {
            var status = _ProofStatus(proof, label);
            return status == "elevated" || status == "high";
        });
    }

    private static bool _EvidenceHas(List<DiagnosticEvidenceItem> evidence, params string[] words)
    {
        var combined = string.Join(" ", evidence.Select(e => $"{e.Label} {e.Value}"));
        return _Has(combined, words);
    }

    public static DiagnosticResult RunDiagnostic(DiagnosticSymptomPayload sym, HardwareMetrics metrics)
    {
        var warningText = _JoinedText(sym.WarningSigns, sym.WarningSignsLabel, sym.SystemState);
        var recentChangesText = _JoinedText(sym.RecentChanges, sym.RecentChangesLabel, sym.SystemState);
        var activityText = _JoinedText(sym.AffectedActivity, sym.AffectedActivityLabel, sym.SystemState);

        var proof = new List<DiagnosticProofItem>();

        var cpuStatus = metrics.CpuUsagePercent >= 85 ? "high" : metrics.CpuUsagePercent >= 70 ? "elevated" : "normal";
        var ramStatus = metrics.RamUsagePercent >= 85 ? "high" : metrics.RamUsagePercent >= 75 ? "elevated" : "normal";
        var diskStatus = metrics.DiskUsagePercent >= 90 ? "high" : metrics.DiskUsagePercent >= 80 ? "elevated" : "normal";

        string browserStatus = "normal";
        if (metrics.BrowserMemoryMb >= 2048 || metrics.BrowserProcessCount >= 20) browserStatus = "high";
        else if (metrics.BrowserHeavy || metrics.BrowserMemoryMb >= 1024 || metrics.BrowserProcessCount >= 10) browserStatus = "elevated";
        else if (metrics.BrowserMemoryMb > 0) browserStatus = "observed";

        string gameStatus = metrics.GameDetected ? "observed" : "normal";
        string gameValue = metrics.GameProcesses.Any() ? string.Join(", ", metrics.GameProcesses.Take(3)) : "None detected";

        proof.Add(_MakeProof("CPU load", $"{metrics.CpuUsagePercent}%", cpuStatus, "High CPU load can cause stuttering, freezing, and slow response."));
        proof.Add(_MakeProof("RAM usage", $"{metrics.RamUsagePercent}%", ramStatus, "High RAM usage can cause freezing, slow app switching, and browser lag."));
        proof.Add(_MakeProof("Storage usage", $"{metrics.DiskUsagePercent}% full", diskStatus, "Very full storage can slow updates, file access, and system maintenance."));
        proof.Add(_MakeProof("Browser workload", $"{metrics.BrowserMemoryMb} MB across {metrics.BrowserProcessCount} processes", browserStatus, "Heavy browser memory usage can explain slowdowns with many tabs or web apps."));
        proof.Add(_MakeProof("Game or launcher process", gameValue, gameStatus, "Game or launcher processes can increase GPU, CPU, memory, and heat while they are open."));

        if (metrics.CpuThermalThrottling)
        {
            proof.Add(_MakeProof("CPU Thermal Throttling", "Detected (CPU downclocking under heavy load)", "high", "The processor is overheating and slowing itself down to prevent damage. This causes severe lag."));
        }
        if (metrics.SmartDriveWarning)
        {
            proof.Add(_MakeProof("SMART Drive Warning", "Imminent hardware failure reported", "high", "The storage drive's internal sensors are reporting it may fail soon. Data loss is a risk."));
        }
        if (!string.IsNullOrEmpty(metrics.MemoryLeakWarning))
        {
            proof.Add(_MakeProof("Suspicious Memory Usage", metrics.MemoryLeakWarning, "high", "A single process is using an extreme amount of memory, which may indicate a software bug or memory leak."));
        }

        var score = new Dictionary<string, int>
        {
            { "OS performance degradation", 0 },
            { "Driver conflict", 0 },
            { "Storage health behavior", 0 },
            { "Boot and startup failure", 0 },
            { "Display driver behavior", 0 },
            { "Thermal condition", 0 },
            { "System log event flags", 0 }
        };

        var evidence = new List<DiagnosticEvidenceItem>();
        var reportedCategory = _ReportedCategory(sym.SymptomType);

        bool criticalWarning = _Has(warningText, 
            "blue screen", "bsod", "stop code", "no display", "black screen", "screen stays black", 
            "screen remains black", "remains black", "screen goes black", "no boot", "boot loop", 
            "startup repair", "smart", "caution", "bad", "access denied", "file not found", "display driver") ||
            sym.MentionsBlackScreen || sym.MentionsNoBoot || sym.MentionsBlueScreen || sym.MentionsStorage;

        bool objectiveProblemFound = proof.Any(p => p.Status == "elevated" || p.Status == "high");
        bool strongUserWarning = criticalWarning || (sym.Severity == "high" && (sym.Frequency == "frequent" || sym.Frequency == "always"));

        if (reportedCategory != null)
        {
            _Add(score, evidence, reportedCategory, strongUserWarning ? 2 : 1, "Reported symptom", sym.SymptomType);
        }

        if (reportedCategory == "Storage health behavior")
        {
            _Add(score, evidence, "Storage health behavior", 3, "Storage issue selected", sym.SymptomType);
        }

        if (sym.Severity == "high" && reportedCategory != null)
        {
            _Add(score, evidence, reportedCategory, 1, "High disruption", sym.Severity);
        }

        if ((sym.Frequency == "frequent" || sym.Frequency == "always") && reportedCategory != null)
        {
            _Add(score, evidence, reportedCategory, 1, "Frequent issue", sym.Frequency);
        }

        if (reportedCategory == "Storage health behavior")
        {
            if (_Has(activityText, "moving files", "opening folders", "saving work", "saving", "save", "file access", "folders", "files", "loading maps", "textures"))
                _Add(score, evidence, "Storage health behavior", 4, "Storage activity", !string.IsNullOrEmpty(sym.AffectedActivityLabel) ? sym.AffectedActivityLabel : sym.AffectedActivity);

            if (_Has(warningText, "searching", "saving", "save", "file", "folder", "access denied", "file not found", "drive", "disk", "storage", "freezes while searching"))
                _Add(score, evidence, "Storage health behavior", 4, "Storage warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : (!string.IsNullOrEmpty(sym.WarningSigns) ? sym.WarningSigns : sym.SystemState));

            if (_Has(recentChangesText, "huge program", "configuration archives", "large program", "new software", "new software installed", "downloaded"))
                _Add(score, evidence, "Storage health behavior", 2, "Recent storage-heavy change", !string.IsNullOrEmpty(sym.RecentChangesLabel) ? sym.RecentChangesLabel : sym.RecentChanges);
        }

        if (metrics.CpuUsagePercent >= 85) _Add(score, evidence, "OS performance degradation", 4, "CPU pressure detected", $"{metrics.CpuUsagePercent}%", "Live scan");
        else if (metrics.CpuUsagePercent >= 70) _Add(score, evidence, "OS performance degradation", 2, "CPU pressure detected", $"{metrics.CpuUsagePercent}%", "Live scan");

        if (metrics.RamUsagePercent >= 85) _Add(score, evidence, "OS performance degradation", 5, "RAM pressure detected", $"{metrics.RamUsagePercent}%", "Live scan");
        else if (metrics.RamUsagePercent >= 75) _Add(score, evidence, "OS performance degradation", 3, "RAM pressure detected", $"{metrics.RamUsagePercent}%", "Live scan");

        if (browserStatus == "high") _Add(score, evidence, "OS performance degradation", 4, "Heavy browser workload detected", $"{metrics.BrowserMemoryMb} MB across {metrics.BrowserProcessCount} processes", "Live scan");
        else if (browserStatus == "elevated") _Add(score, evidence, "OS performance degradation", 2, "Browser workload elevated", $"{metrics.BrowserMemoryMb} MB across {metrics.BrowserProcessCount} processes", "Live scan");

        if (metrics.DiskUsagePercent >= 90)
        {
            _Add(score, evidence, "Storage health behavior", 4, "Storage pressure detected", $"{metrics.DiskUsagePercent}% full", "Live scan");
            _Add(score, evidence, "OS performance degradation", 2, "Storage pressure detected", $"{metrics.DiskUsagePercent}% full", "Live scan");
        }
        else if (metrics.DiskUsagePercent >= 80)
        {
            _Add(score, evidence, "OS performance degradation", 1, "Storage usage elevated", $"{metrics.DiskUsagePercent}% full", "Live scan");
        }

        if (_Text(metrics.StorageType) == "hdd" && (sym.AffectedActivity == "startup" || sym.AffectedActivity == "working"))
        {
            _Add(score, evidence, "OS performance degradation", 1, "Storage type", "HDD can contribute to slow loading", "Detected component");
        }

        if (metrics.GameDetected && sym.AffectedActivity == "gaming")
        {
            if (reportedCategory == "Thermal condition" || sym.MentionsHeat)
                _Add(score, evidence, "Thermal condition", 2, "Game workload observed", gameValue, "Live scan");

            if (reportedCategory == "Display driver behavior" || sym.MentionsFlicker)
                _Add(score, evidence, "Display driver behavior", 2, "Graphics workload observed", gameValue, "Live scan");
        }

        if (metrics.CpuThermalThrottling)
        {
            _Add(score, evidence, "Thermal condition", 8, "Thermal throttling detected", "CPU downclocking under load", "Live scan");
            _Add(score, evidence, "OS performance degradation", 4, "Performance drop", "Thermal throttling", "Live scan");
        }
        
        if (metrics.SmartDriveWarning)
        {
            _Add(score, evidence, "Storage health behavior", 10, "SMART failure predicted", "Drive reported failure risk", "Live scan");
        }
        
        if (!string.IsNullOrEmpty(metrics.MemoryLeakWarning))
        {
            _Add(score, evidence, "OS performance degradation", 5, "Memory leak detected", "Process consuming abnormal RAM", "Live scan");
        }

        if (_Has(warningText, "blue screen", "bsod", "error code", "stop code") || sym.MentionsBlueScreen)
            _Add(score, evidence, "Driver conflict", 5, "Crash warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : (!string.IsNullOrEmpty(sym.WarningSigns) ? sym.WarningSigns : sym.SystemState));

        if (_Has(warningText, "display driver", "screen flicker", "visual", "flicker", "screen tearing", "glitch") || sym.MentionsFlicker)
            _Add(score, evidence, "Display driver behavior", 5, "Display warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : (!string.IsNullOrEmpty(sym.WarningSigns) ? sym.WarningSigns : sym.SystemState));

        if (_Has(warningText, "smart", "caution", "bad", "file not found", "access denied") || sym.MentionsStorage)
            _Add(score, evidence, "Storage health behavior", 5, "Storage warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : (!string.IsNullOrEmpty(sym.WarningSigns) ? sym.WarningSigns : sym.SystemState));

        if (_Has(warningText, "startup repair", "no boot device", "black screen", "no display", "remains black", "screen goes black", "screen stays black") || sym.MentionsBlackScreen || sym.MentionsNoBoot)
            _Add(score, evidence, "Boot and startup failure", 6, "Boot warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : (!string.IsNullOrEmpty(sym.WarningSigns) ? sym.WarningSigns : sym.SystemState));

        if ((_Has(warningText, "loud fan noise", "hot", "overheat", "overheating", "very warm") || sym.MentionsHeat) && reportedCategory == "Thermal condition")
            _Add(score, evidence, "Thermal condition", 4, "Heat or cooling warning", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : sym.SystemState);

        if ((_Has(warningText, "slow", "lag", "freeze", "freezing", "stutter", "stuttering") || sym.MentionsSlow) && reportedCategory == "OS performance degradation")
            _Add(score, evidence, "OS performance degradation", 3, "Slowdown described", !string.IsNullOrEmpty(sym.SystemState) ? sym.SystemState : sym.WarningSignsLabel);

        if (reportedCategory == "Boot and startup failure")
        {
            if (_Has(activityText, "startup", "boot", "sleep", "hibernate", "hibernation", "turning on", "resume"))
                _Add(score, evidence, "Boot and startup failure", 3, "Boot timing", !string.IsNullOrEmpty(sym.AffectedActivityLabel) ? sym.AffectedActivityLabel : sym.AffectedActivity);

            if (_Has(warningText, "black screen", "remains black", "screen goes black", "no boot", "no boot device", "startup repair"))
                _Add(score, evidence, "Boot and startup failure", 5, "Boot warning detail", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : sym.WarningSigns);
        }

        if (reportedCategory == "Display driver behavior")
        {
            if (_Has(warningText, "flicker", "screen", "display driver", "visual", "glitch"))
                _Add(score, evidence, "Display driver behavior", 4, "Display warning detail", !string.IsNullOrEmpty(sym.WarningSignsLabel) ? sym.WarningSignsLabel : sym.WarningSigns);
        }

        if (_Has(recentChangesText, "driver update") && (reportedCategory == "Driver conflict" || reportedCategory == "Display driver behavior"))
            _Add(score, evidence, reportedCategory, 2, "Recent driver change", !string.IsNullOrEmpty(sym.RecentChangesLabel) ? sym.RecentChangesLabel : sym.RecentChanges);

        if (_Has(recentChangesText, "windows update") && (reportedCategory == "OS performance degradation" || reportedCategory == "Boot and startup failure" || reportedCategory == "Driver conflict"))
            _Add(score, evidence, reportedCategory, 1, "Recent Windows update", !string.IsNullOrEmpty(sym.RecentChangesLabel) ? sym.RecentChangesLabel : sym.RecentChanges);

        if (_Has(recentChangesText, "hardware upgrade") && (reportedCategory == "Storage health behavior" || reportedCategory == "Boot and startup failure" || reportedCategory == "Thermal condition"))
            _Add(score, evidence, reportedCategory, 1, "Recent hardware change", !string.IsNullOrEmpty(sym.RecentChangesLabel) ? sym.RecentChangesLabel : sym.RecentChanges);

        var ranked = score.OrderByDescending(x => x.Value).ToList();
        var diagnosedCategory = ranked[0].Key;
        var topScore = ranked[0].Value;
        var secondScore = ranked[1].Value;
        var actionCategory = "";
        var confidenceLabel = "";

        if (!objectiveProblemFound && !strongUserWarning && topScore < 5)
        {
            diagnosedCategory = "No active issue detected";
            actionCategory = "Monitor";
            confidenceLabel = "High";
        }
        else
        {
            if (topScore >= 9 && topScore - secondScore >= 2) confidenceLabel = "High";
            else if (topScore >= 5) confidenceLabel = "Moderate";
            else confidenceLabel = "Low";

            if (topScore < 4 && !strongUserWarning)
            {
                diagnosedCategory = "No active issue detected";
                actionCategory = "Monitor";
                confidenceLabel = "Moderate";
            }
            else if (diagnosedCategory == "Driver conflict" || diagnosedCategory == "Boot and startup failure" || diagnosedCategory == "Display driver behavior" || diagnosedCategory == "System log event flags")
                actionCategory = "Troubleshoot";
            else if (diagnosedCategory == "OS performance degradation" || diagnosedCategory == "Thermal condition" || diagnosedCategory == "Storage health behavior")
                actionCategory = "Maintain";
            else
                actionCategory = "Monitor";

            if (diagnosedCategory == "Boot and startup failure" && (sym.Severity == "high" || _Has(warningText, "no display", "black screen", "no boot device", "startup repair") || sym.MentionsBlackScreen || sym.MentionsNoBoot))
            {
                actionCategory = "Escalate for Professional Inspection";
                confidenceLabel = "High";
            }
            
            if (metrics.SmartDriveWarning)
            {
                actionCategory = "Escalate for Professional Inspection";
                confidenceLabel = "High";
            }
        }

        var evidenceForResult = evidence.Where(e => e.Category == diagnosedCategory).Take(6).ToList();
        var displayProof = _SelectDisplayProof(diagnosedCategory, proof, evidenceForResult);
        var target = _GetVerificationTarget(diagnosedCategory, proof, evidenceForResult);
        var nextStep = _GetRecommendedNextStep(diagnosedCategory, actionCategory, proof, evidenceForResult);

        return new DiagnosticResult
        {
            DiagnosedCategory = diagnosedCategory,
            ActionCategory = actionCategory,
            ConfidenceLabel = confidenceLabel,
            Evidence = evidenceForResult,
            Proof = displayProof,
            AllLiveProof = proof,
            Target = target,
            RecommendedNextStep = nextStep
        };
    }

    private static DiagnosticProofItem _MakeUserProof(string label, string? value, string meaning)
    {
        return _MakeProof(label, string.IsNullOrEmpty(value) ? "Not specified" : value, "detected", meaning);
    }

    private static List<DiagnosticProofItem> _SelectDisplayProof(string category, List<DiagnosticProofItem> proof, List<DiagnosticEvidenceItem> evidence)
    {
        var display = new List<DiagnosticProofItem>();
        var cat = category.ToLower();

        if (cat.Contains("no active issue"))
        {
            return proof.Where(p => p.Label == "CPU load" || p.Label == "RAM usage" || p.Label == "Storage usage" || p.Label == "Browser workload").ToList();
        }

        if (cat.Contains("os performance"))
        {
            display.AddRange(proof.Where(p => (p.Label == "CPU load" || p.Label == "RAM usage" || p.Label == "Browser workload" || p.Label == "Storage usage") && (p.Status == "elevated" || p.Status == "high")));
        }
        else if (cat.Contains("thermal"))
        {
            var game = proof.FirstOrDefault(p => p.Label == "Game or launcher process");
            if (game != null && game.Status != "normal") display.Add(game);
            
            var cpu = proof.FirstOrDefault(p => p.Label == "CPU load");
            if (cpu != null && (cpu.Status == "elevated" || cpu.Status == "high")) display.Add(cpu);
        }
        else if (cat.Contains("storage"))
        {
            var storage = proof.FirstOrDefault(p => p.Label == "Storage usage");
            if (storage != null)
            {
                if (storage.Status == "elevated" || storage.Status == "high") display.Add(storage);
                else display.Add(_MakeProof("Storage space checked", storage.Value, "observed", "The drive is not very full right now, so this result is based more on your file-access symptoms than low free space."));
            }
        }

        foreach (var item in evidence)
        {
            if (item.Source == "Live scan") continue;
            if (item.Category.ToLower() == cat)
                display.Add(_MakeUserProof(item.Label, item.Value, "This answer directly influenced the diagnostic result."));
        }

        if (!display.Any())
        {
            display.Add(_MakeUserProof("Submitted symptom pattern", category, "The result is mainly based on your selected answers and written description, not a strong live hardware reading."));
        }

        return display.Take(6).ToList();
    }

    private static string _GetRecommendedNextStep(string category, string actionCategory, List<DiagnosticProofItem> proof, List<DiagnosticEvidenceItem> evidence)
    {
        var cat = category.ToLower();

        if (cat.Contains("no active issue")) return "No active pressure showed up in this scan. Use the computer normally, and run another diagnosis while the symptom is happening if it comes back.";
        if (cat.Contains("boot") || cat.Contains("startup"))
        {
            if (_EvidenceHas(evidence, "black screen", "no display", "no boot", "startup repair"))
                return "If the screen stays black or Windows cannot fully start, stop repeated restarts. Try Windows Startup Repair if available, or ask a technician to inspect it. If Windows still opens, check Startup Apps and recent Windows updates.";
            return "If Windows still opens, review Startup Apps first. If the startup delay continues, restart once and run the diagnosis again while the issue is happening.";
        }
        if (cat.Contains("os performance"))
        {
            if (_HasPressure(proof, "RAM usage", "Browser workload"))
                return "Close unused browser tabs and heavy apps first, then use Verify & Inspect to open Task Manager. If the PC still feels slow, apply the available safe maintenance actions and run the diagnosis again.";
            return "Review startup apps and temporary files, then restart the PC and run the diagnosis again while the slowdown is happening.";
        }
        if (cat.Contains("thermal")) return "Pause games or heavy apps, let the PC cool for a few minutes, and check that vents and fans are not blocked. If it shuts down, smells hot, or shows no display, stop testing and ask a technician to inspect it.";
        if (cat.Contains("driver")) return "Open Device Manager and look for warning icons. Only update, roll back, or reinstall the device that matches the symptom, especially if the problem started after a driver or Windows update.";
        if (cat.Contains("display")) return "Try the safe display-driver reset shortcut first. If flickering or black screens continue, open Device Manager and review the display adapter driver.";
        if (cat.Contains("storage")) return "Back up important files first. Then open Storage settings or run the read-only disk scan. Do not run repair commands until your important files are backed up.";
        if (actionCategory.ToLower().Contains("escalate")) return "Stop repeated testing and bring this result to a qualified technician, especially if the computer cannot boot, overheats, or shows hardware warning signs.";
        
        return "Review the proof shown above, use the safest available action, and run the diagnosis again after the change.";
    }

    private static VerificationTarget _GetVerificationTarget(string category, List<DiagnosticProofItem> proof, List<DiagnosticEvidenceItem> evidence)
    {
        var cat = category.ToLower();

        if (cat.Contains("no active issue"))
            return new VerificationTarget { Target = "none", Label = "No verification target needed", Description = "The live scan did not find an active issue that needs a Windows tool right now." };

        if (cat.Contains("boot") || cat.Contains("startup"))
        {
            if (_EvidenceHas(evidence, "black screen", "no display", "no boot", "startup repair"))
                return new VerificationTarget { Target = "reliability_monitor", Label = "Windows Reliability Monitor", Description = "Use this if Windows still opens. It can show recent startup failures, crashes, or update-related problems." };
            return new VerificationTarget { Target = "startup_apps", Label = "Windows Startup Apps settings", Description = "Review apps that run during startup." };
        }
        if (cat.Contains("os performance"))
        {
            if (_HasPressure(proof, "RAM usage", "CPU load", "Browser workload"))
                return new VerificationTarget { Target = "task_manager", Label = "Task Manager - Processes tab", Description = "Check which apps are using the most memory, CPU, or browser processes." };
            return new VerificationTarget { Target = "startup_apps", Label = "Windows Startup Apps settings", Description = "Review apps that automatically run when Windows starts." };
        }
        if (cat.Contains("thermal"))
            return new VerificationTarget { Target = "task_manager", Label = "Task Manager - Performance tab", Description = "Check whether a game, browser, or app is keeping the PC under heavy load." };
        if (cat.Contains("storage"))
            return new VerificationTarget { Target = "storage_settings", Label = "Windows Storage settings", Description = "Inspect drive space before running any read-only disk check." };
        if (cat.Contains("display") || cat.Contains("driver"))
            return new VerificationTarget { Target = "device_manager", Label = "Device Manager", Description = "Inspect the driver without changing it automatically." };

        return new VerificationTarget { Target = "reliability_monitor", Label = "Windows Reliability Monitor", Description = "Review recent crashes and Windows error events." };
    }
}
