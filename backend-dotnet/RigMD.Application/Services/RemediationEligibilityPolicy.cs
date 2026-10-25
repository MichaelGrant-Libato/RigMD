using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;

namespace RigMD.Application.Services;

public sealed record RemediationEligibility(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("newer_session_id")] string? NewerSessionId = null,
    [property: JsonPropertyName("checked_at")] string? CheckedAt = null);

public static class RemediationEligibilityPolicy
{
    private sealed record Reading(string Metric, string Status, bool Verified);
    private sealed record Check(DiagnosticSessionDto Session, DateTimeOffset At, Reading[] Rows, string DeviceId);

    public static RemediationEligibility Evaluate(DiagnosticSessionDto current, IEnumerable<DiagnosticSessionDto> history)
    {
        var check = Snapshot(current);
        var targets = Read(current.Proof).Concat(check?.Rows ?? [])
            .Where(r => r.Verified && Warning(r.Status)).Select(r => r.Metric).Distinct(StringComparer.Ordinal).ToArray();
        if (targets.Length == 0)
            return NotIndicated("This check has no verified warning that calls for an action.", check?.At);
        if (check == null)
            return Recheck("This saved check's time cannot be verified. Run a new check on this computer.");
        if (string.IsNullOrWhiteSpace(check.DeviceId))
            return targets.All(key => State(check.Rows, key) == "normal")
                ? NotIndicated("The warning is not detected in this check's latest readings. No action is indicated.", check.At)
                : Recheck("This saved check's device cannot be verified. Run a new check on this computer.", check.At);
        if (targets.Any(key => !Covers(current, key)))
            return Recheck("This result has no verified warning in a known check scope. Check the current status before choosing an action.", check.At);

        // Never use account IDs, the literal agent ID "local", or CPU/OS profile IDs as device identity.
        var relevant = history.Where(s => s.SessionId != current.SessionId &&
            (string.IsNullOrWhiteSpace(Snapshot(s)?.DeviceId) || string.Equals(Snapshot(s)?.DeviceId, check.DeviceId, StringComparison.Ordinal)) &&
            targets.Any(key => Covers(s, key))).ToArray();
        if (relevant.Any(s => Snapshot(s) == null ||
            !string.IsNullOrWhiteSpace(s.ResolutionCheckedAt) &&
            (!DateTimeOffset.TryParse(s.ResolutionCheckedAt, out var at) ||
             !DateTimeOffset.TryParse(s.CreatedAt, out var created) || at < created)))
            return Recheck("The time of a matching check could not be verified. Run a new check on this computer.");
        var later = relevant
            .Select(Snapshot).OfType<Check>()
            .Where(c => c.At >= check.At && targets.Any(key => Covers(c.Session, key)))
            .Where(c => string.IsNullOrWhiteSpace(c.DeviceId) ||
                string.Equals(c.DeviceId, check.DeviceId, StringComparison.Ordinal))
            .OrderByDescending(c => c.At).ToArray();
        if (later.Length > 0)
        {
            var latest = later[0];
            if (later.Any(c => c.At == latest.At && c.Session.SessionId != latest.Session.SessionId) ||
                latest.At == check.At || string.IsNullOrWhiteSpace(latest.DeviceId))
                return Recheck("A later matching check has ambiguous device identity or ordering. Run a new check on this computer.", latest.At);
            var matching = targets.Where(key => Covers(latest.Session, key)).ToArray();
            if (matching.Any(key => State(latest.Rows, key) == "unknown"))
                return Recheck("The latest matching check could not verify this warning. Check the current status before choosing an action.", latest.At);
            var message = matching.All(key => State(latest.Rows, key) == "normal")
                ? "The warning is not detected in the newer matching check. No action is indicated by the older warning. Open the newer result."
                : "A newer matching check is available. Open the newer result and review its current status before choosing an action.";
            return new("superseded", message,
                latest.Session.SessionId, latest.At.ToString("o"));
        }

        if (targets.All(key => State(check.Rows, key) == "normal"))
            return NotIndicated("The warning is not detected in this check's latest readings. No action is indicated.", check.At);
        if (targets.Any(key => State(check.Rows, key) == "unknown"))
            return Recheck("This warning is no longer verified by this check's latest readings. Check the current status before choosing an action.", check.At);
        return new("allowed", "The latest matching saved check supports reviewing an action. A fresh approval is still required.", CheckedAt: check.At.ToString("o"));
    }

    private static RemediationEligibility Recheck(string message, DateTimeOffset? at = null) =>
        new("recheck_required", message, CheckedAt: at?.ToString("o"));

    private static RemediationEligibility NotIndicated(string message, DateTimeOffset? at = null) =>
        new("not_indicated", message, CheckedAt: at?.ToString("o"));

    private static Check? Snapshot(DiagnosticSessionDto session)
    {
        if (!DateTimeOffset.TryParse(session.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created)) return null;
        if (session.ResolutionStatus == "needs_recheck" && session.ResolutionProof.Length == 0)
            return new(session, created, [], session.DeviceId);
        if (!string.IsNullOrWhiteSpace(session.ResolutionCheckedAt))
        {
            if (!DateTimeOffset.TryParse(session.ResolutionCheckedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) || at < created)
                return new(session, created, [], session.DeviceId);
            return new(session, at, Read(session.ResolutionProof),
                string.IsNullOrWhiteSpace(session.ResolutionDeviceId) ? session.DeviceId : session.ResolutionDeviceId);
        }
        return new(session, created, Read(session.Proof), session.DeviceId);
    }

    private static Reading[] Read(object[] proof) => proof.Select(item =>
    {
        var row = JsonSerializer.SerializeToElement(item);
        string Text(string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? "" : "";
        var metric = Text("metric_id");
        var value = Text("value");
        var status = Text("status").ToLowerInvariant();
        var verified = metric.Length > 0 && value.Length > 0 &&
            !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("not available", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("not verified", StringComparison.OrdinalIgnoreCase) &&
            Text("evidence_kind") is "measurement" or "reported_status" or "event_log" &&
            (Warning(status) || status is "normal" or "normal now");
        // Positional device indexes cannot identify the same device across scans.
        if (metric.StartsWith("device_error_", StringComparison.Ordinal) && int.TryParse(metric[13..], out _)) verified = false;
        return new Reading(metric, status, verified);
    }).ToArray();

    private static bool Warning(string status) => status is "high" or "elevated" or "still needs attention";

    private static string State(Reading[] rows, string metric)
    {
        var matching = rows.Where(r => r.Metric == metric).ToArray();
        if (matching.Length == 0 || matching.Any(r => !r.Verified)) return "unknown";
        return matching.Any(r => Warning(r.Status)) ? "warning" : "normal";
    }

    private static bool Covers(DiagnosticSessionDto session, string metric)
    {
        var component = metric switch
        {
            "memory_usage" or "memory_app_warning" => "memory",
            "cpu_usage" or "cpu_throttling" => "cpu",
            "cpu_temperature" => "thermal",
            "gpu_temperature" or "graphics_devices" => "gpu",
            "storage_usage" or "storage_health" => "storage",
            "battery_charge" => "battery",
            _ when metric.StartsWith("network_", StringComparison.Ordinal) => "network",
            _ when metric.StartsWith("device_error_", StringComparison.Ordinal) => "drivers",
            _ => ""
        };
        if (component.Length == 0) return false;
        switch (session.DiagnosisMode.Trim().ToLowerInvariant())
        {
            case "full": return true;
            case "component":
                string[] parts;
                try
                {
                    parts = session.ComponentIds.TrimStart().StartsWith('[')
                        ? JsonSerializer.Deserialize<string[]>(session.ComponentIds) ?? []
                        : session.ComponentIds.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                }
                catch (JsonException) { return false; }
                return parts.Any(p => DiagnosticScopeMapper.NormalizeComponentId(p) == component ||
                    component == "thermal" && DiagnosticScopeMapper.NormalizeComponentId(p) == "cpu");
            case "scenario":
                return DiagnosticScopeMapper.NormalizeScenarioId(session.ScenarioId) switch
                {
                    "slow-system" or "stuttering-freezing" => component is "cpu" or "memory" or "storage" or "thermal" or "gpu",
                    "slow-boot" or "storage-problem" => component == "storage",
                    "overheating-loud-fan" => component is "cpu" or "thermal" or "gpu",
                    "network-problem" => component == "network",
                    "rapid-battery-drain" => component is "battery" or "cpu",
                    "driver-error" => component is "drivers" or "gpu",
                    "no-display" => component == "gpu",
                    _ => false
                };
            default: return false;
        }
    }
}
