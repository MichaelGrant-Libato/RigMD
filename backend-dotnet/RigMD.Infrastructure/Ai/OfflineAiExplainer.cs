using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Ai;
using RigMD.Domain.Rules;

namespace RigMD.Infrastructure.Ai;

public class OfflineAiExplainer : IAiExplainer
{
    public Task<string> GenerateExplanationAsync(DiagnosticResult result, DiagnosticSymptomPayload symptomPayload)
    {
        if (result.DiagnosedCategory.Equals("No active issue detected", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(BuildNoIssueExplanation(result.Proof));
        }

        return Task.FromResult(
            BuildPlainExplanation(
                result.DiagnosedCategory,
                result.ActionCategory,
                result.ConfidenceLabel,
                result.Evidence,
                result.AllLiveProof));
    }

    private string BuildNoIssueExplanation(List<DiagnosticProofItem> proof)
    {
        var normalItems = proof
            .Where(p => p.Status.Equals("normal", StringComparison.OrdinalIgnoreCase) ||
                        p.Status.Equals("observed", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Label} is {p.Value}")
            .ToList();

        var detail = normalItems.Any()
            ? string.Join(", ", normalItems.Take(4))
            : "live system readings are within normal ranges";

        return $"RigMD did not find an active problem in the live scan right now ({detail}). If the symptom only happens occasionally, run another check while the issue is actively occurring or use the guided inspection below.";
    }

    private string BuildPlainExplanation(
        string category,
        string actionCategory,
        string confidenceLabel,
        List<DiagnosticEvidenceItem> evidence,
        List<DiagnosticProofItem> proof)
    {
        var cat = (category ?? string.Empty).ToLowerInvariant();
        var liveFindings = new List<string>();

        DiagnosticProofItem? FindProof(params string[] labels)
        {
            return proof.FirstOrDefault(x =>
                labels.Any(l => x.Label.Contains(l, StringComparison.OrdinalIgnoreCase)));
        }

        bool HasPressure(params string[] labels)
        {
            var p = FindProof(labels);
            return p != null && (p.Status.Equals("elevated", StringComparison.OrdinalIgnoreCase) ||
                                 p.Status.Equals("high", StringComparison.OrdinalIgnoreCase));
        }

        string ProofValue(params string[] labels)
        {
            return FindProof(labels)?.Value ?? string.Empty;
        }

        var cpuVal = ProofValue("CPU load", "Processor (CPU)");
        var ramVal = ProofValue("RAM usage", "Physical Memory (RAM)");
        var storageVal = ProofValue("Storage usage", "Storage & S.M.A.R.T.");
        var gpuVal = ProofValue("Graphics & Drivers");
        var netVal = ProofValue("Network Connectivity");

        if (cat.Contains("application crash") || cat.Contains("system crash") || cat.Contains("stop error"))
        {
            if (!string.IsNullOrEmpty(cpuVal)) liveFindings.Add($"CPU is at {cpuVal}");
            if (!string.IsNullOrEmpty(ramVal)) liveFindings.Add($"RAM is at {ramVal}");
            if (!string.IsNullOrEmpty(gpuVal)) liveFindings.Add($"graphics/driver status is {gpuVal}");
        }
        else if (cat.Contains("os performance") || cat.Contains("memory") || cat.Contains("resource exhaustion"))
        {
            if (HasPressure("CPU load", "Processor (CPU)") && !string.IsNullOrEmpty(cpuVal))
                liveFindings.Add($"CPU load is {cpuVal}");
            if (!string.IsNullOrEmpty(ramVal))
                liveFindings.Add($"RAM usage is {ramVal}");
            if (HasPressure("Browser workload"))
                liveFindings.Add($"browser workload is {ProofValue("Browser workload")}");
            if (HasPressure("Storage usage", "Storage & S.M.A.R.T.") && !string.IsNullOrEmpty(storageVal))
                liveFindings.Add($"storage is {storageVal}");
        }
        else if (cat.Contains("boot") || cat.Contains("startup"))
        {
            if (!string.IsNullOrEmpty(storageVal)) liveFindings.Add($"storage is {storageVal}");
            if (!string.IsNullOrEmpty(ramVal)) liveFindings.Add($"RAM usage is {ramVal}");
        }
        else if (cat.Contains("storage"))
        {
            if (!string.IsNullOrEmpty(storageVal)) liveFindings.Add($"storage is {storageVal}");
        }
        else if (cat.Contains("thermal") || cat.Contains("cpu"))
        {
            if (!string.IsNullOrEmpty(cpuVal)) liveFindings.Add($"CPU is at {cpuVal}");
            var game = ProofValue("Game or launcher process");
            if (!string.IsNullOrEmpty(game) && game != "None detected")
                liveFindings.Add($"active workload detected: {game}");
        }
        else if (cat.Contains("driver") || cat.Contains("display"))
        {
            if (!string.IsNullOrEmpty(gpuVal)) liveFindings.Add($"graphics and driver status shows {gpuVal}");
        }
        else if (cat.Contains("network"))
        {
            if (!string.IsNullOrEmpty(netVal)) liveFindings.Add($"network connectivity shows {netVal}");
        }

        string reason;
        if (liveFindings.Any())
        {
            reason = string.Join(", ", liveFindings);
        }
        else if (proof.Any())
        {
            reason = string.Join(", ", proof.Take(3).Select(p => $"{p.Label}: {p.Value}"));
        }
        else if (evidence.Any())
        {
            reason = string.Join(", ", evidence.Take(3).Select(e => $"{e.Label}: {e.Value}"));
        }
        else
        {
            reason = "your selected check points to this area";
        }

        string intro;
        if (cat.Contains("application crash"))
        {
            intro = "When individual programs freeze or close unexpectedly, the cause is usually recorded in Windows Reliability Monitor and Application Event Logs rather than live resource usage alone.";
        }
        else if (cat.Contains("system crash") || cat.Contains("stop error"))
        {
            intro = "Unexpected restarts or blue-screen stop errors typically point to a driver fault, Windows system file issue, or hardware instability logged in Windows Event Viewer.";
        }
        else if (cat.Contains("memory pressure") || cat.Contains("high memory"))
        {
            intro = "Active applications and browser tabs are consuming a high share of physical RAM, which can force Windows to swap data to disk and feel sluggish.";
        }
        else if (cat.Contains("elevated cpu"))
        {
            intro = "Your processor is handling a heavy active workload from running applications or background tasks.";
        }
        else if (category == "OS performance degradation")
        {
            intro = HasPressure("Storage usage", "Storage & S.M.A.R.T.")
                ? "Your computer may feel slow because Windows is under load from memory, background apps, startup programs, or storage usage."
                : "Your computer may feel slow because Windows is under load from memory, active apps, browser tabs, or startup programs.";
        }
        else if (cat.Contains("storage"))
        {
            intro = HasPressure("Storage usage", "Storage & S.M.A.R.T.")
                ? "This points toward high storage usage or a drive health warning, which can slow down file access, updates, and caching."
                : "This check focused on your storage drives, free space, and S.M.A.R.T. health status.";
        }
        else if (category == "Driver conflict")
        {
            intro = "This points to a hardware driver issue where Windows and a connected device or adapter may not be communicating cleanly.";
        }
        else if (category == "Boot and startup failure")
        {
            intro = "Slow startup times are usually caused by too many apps launching at Windows sign-in, accumulated temporary caches, or boot drive load.";
        }
        else if (category == "Display driver behavior")
        {
            intro = "This points to the graphics adapter, display driver, or monitor connection path.";
        }
        else if (category == "Thermal condition")
        {
            intro = "This check focused on processor heat and cooling load. When a PC runs warm, it may lower clock speeds to protect hardware.";
        }
        else if (cat.Contains("network"))
        {
            intro = "This check focused on your network adapter, ping latency, and DNS name resolution.";
        }
        else
        {
            intro = "RigMD evaluated the live readings for your selected check.";
        }

        return $"{intro} Live scan summary: {reason} (certainty: {confidenceLabel.ToLowerInvariant()}). Follow the {actionCategory.ToLowerInvariant()} guidance below and recheck afterward.";
    }
}
