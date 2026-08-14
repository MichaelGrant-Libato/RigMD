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

        return Task.FromResult(BuildPlainExplanation(result.DiagnosedCategory, result.ActionCategory, result.ConfidenceLabel, result.Evidence, result.AllLiveProof));
    }

    private string BuildNoIssueExplanation(List<DiagnosticProofItem> proof)
    {
        var normalItems = proof
            .Where(p => p.Status.Equals("normal", StringComparison.OrdinalIgnoreCase) || p.Status.Equals("observed", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Label} is {p.Value}")
            .ToList();

        var detail = normalItems.Any() 
            ? string.Join(", ", normalItems.Take(4)) 
            : "the live readings are not showing active pressure";

        return $"RigMD did not find an active problem in the live scan right now. {detail}. That means the issue may not be happening at this exact moment, or it may have already cleared after a restart or after a background task finished. Use the computer normally, and run another check while the symptom is actually happening if it comes back.";
    }

    private string BuildPlainExplanation(string category, string actionCategory, string confidenceLabel, List<DiagnosticEvidenceItem> evidence, List<DiagnosticProofItem> proof)
    {
        var cat = category.ToLower();
        var liveFindings = new List<string>();

        bool HasPressure(string label)
        {
            var p = proof.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
            return p != null && (p.Status == "elevated" || p.Status == "high");
        }

        string ProofValue(string label)
        {
            return proof.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        }

        if (cat.Contains("os performance"))
        {
            if (HasPressure("CPU load")) liveFindings.Add($"CPU load is {ProofValue("CPU load")}");
            if (HasPressure("RAM usage")) liveFindings.Add($"RAM usage is {ProofValue("RAM usage")}");
            if (HasPressure("Browser workload")) liveFindings.Add($"browser workload is {ProofValue("Browser workload")}");
            if (HasPressure("Storage usage")) liveFindings.Add($"storage is {ProofValue("Storage usage")}");
        }
        else if (cat.Contains("storage"))
        {
            if (HasPressure("Storage usage")) liveFindings.Add($"storage is {ProofValue("Storage usage")}");
        }
        else if (cat.Contains("thermal"))
        {
            if (HasPressure("CPU load")) liveFindings.Add($"CPU load is {ProofValue("CPU load")}");
            var game = ProofValue("Game or launcher process");
            if (!string.IsNullOrEmpty(game) && game != "None detected") liveFindings.Add($"a game or launcher process was detected: {game}");
        }

        string reason;
        if (liveFindings.Any())
        {
            reason = string.Join(", ", liveFindings);
        }
        else if (evidence.Any())
        {
            reason = string.Join(", ", evidence.Take(3).Select(e => $"{e.Label}: {e.Value}"));
        }
        else
        {
            reason = "your answers point in this direction, but the live scan did not catch a strong matching reading";
        }

        string intro;
        if (category == "OS performance degradation")
            intro = HasPressure("Storage usage") ? "Your computer is most likely slowing down because Windows is under load from memory, apps, browser tabs, startup programs, or storage pressure." : "Your computer is most likely slowing down because Windows is under load from memory, apps, browser tabs, or startup programs.";
        else if (category == "Storage health behavior")
            intro = HasPressure("Storage usage") ? "This points toward storage pressure, so file access, saving, updates, or drive checks may be involved." : "Your answers point toward a storage or file-access slowdown, even though the drive is not currently showing as very full.";
        else if (category == "Driver conflict")
            intro = "This looks like a driver-related issue, which means Windows and one hardware part may not be communicating cleanly.";
        else if (category == "Boot and startup failure")
            intro = "This points toward a startup or wake-from-sleep problem, especially when the screen stays black, startup loops, or Windows does not fully load.";
        else if (category == "Display driver behavior")
            intro = "This looks related to the display path, so the graphics driver, screen output, or a graphics-heavy app may be involved.";
        else if (category == "Thermal condition")
            intro = "This looks like heat or cooling pressure. When a PC gets too warm, it can slow itself down to protect the parts inside.";
        else
            intro = "RigMD found a likely issue based on the live scan and the details you entered.";

        return $"{intro} RigMD chose this result because {reason}. The confidence is {confidenceLabel.ToLower()}, so treat this as the best next clue rather than a guaranteed final answer. The safest path is to follow the {actionCategory.ToLower()} recommendation below and then run the check again after the change.";
    }
}
