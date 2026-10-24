using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RigMD.Application.Models;

namespace RigMD.Application.Services;

public class RecurringPatternService
{
    public static RecurringResponseDto EmptyResponse(string? warning = null) => new() { database_warning = warning };
    private sealed record Observation(RecurringSessionDto Session, string Key, string Label, string Value,
        string Status, DateTime At, bool Measured, string EvidenceKind = "", bool FollowUpEvents = false);
    private static bool Warning(string status) => status.ToLowerInvariant() is "high" or "elevated" or "still needs attention";
    private static bool Normal(string status) => status.ToLowerInvariant() is "normal" or "normal now";

    private static IEnumerable<Observation> Read(RecurringSessionDto session, object[] proof, DateTime at)
    {
        foreach (var item in proof)
        {
            var row = JsonSerializer.SerializeToElement(item);
            if (row.ValueKind != JsonValueKind.Object) continue;
            string Text(string key) => row.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? "" : "";
            var metric = Text("metric_id");
            var label = Text("label");
            if (string.IsNullOrWhiteSpace(metric) || string.IsNullOrWhiteSpace(label)) continue;
            var value = Text("value");
            var unmeasured = string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || value.Contains("not available", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("not verified", StringComparison.OrdinalIgnoreCase);
            // Device row indexes are not identities: use the reported device name instead.
            var key = metric.StartsWith("device_error_", StringComparison.Ordinal) && int.TryParse(metric[13..], out _)
                ? "device:" + label.Trim().ToLowerInvariant() + ":" + Text("value").Trim().ToLowerInvariant() : metric;
            yield return new(session, key, label, value, unmeasured ? "unknown" : Text("status"), at, true, Text("evidence_kind"),
                Text("window_kind") == "since_diagnosis" || label.Contains("since diagnosis", StringComparison.OrdinalIgnoreCase));
        }
    }

    public RecurringResponseDto BuildPatterns(IReadOnlyList<RecurringSessionDto> sessions)
    {
        var unique = sessions.GroupBy(s => s.Id).Select(g => g.First()).ToArray();
        var initial = new List<Observation>();
        var latest = new List<Observation>();
        foreach (var session in unique)
        {
            var at = session.CreatedAt ?? DateTime.MinValue;
            var rows = Read(session, session.Proof, at).ToArray();
            initial.AddRange(rows);
            latest.AddRange(rows);
            if (DateTimeOffset.TryParse(session.ResolutionCheckedAt, out var checkedAt) && checkedAt.UtcDateTime >= at)
                latest.AddRange(Read(session, session.ResolutionProof, checkedAt.UtcDateTime));
            // Legacy reports are not measured hardware faults.
            if (session.Proof.Length == 0 && IsLegacyConcern(session))
            {
                var key = "reported:" + session.DiagnosedCategory.Trim().ToLowerInvariant() + ":" + session.SymptomType.Trim().ToLowerInvariant();
                initial.Add(new(session, key, session.DiagnosedCategory, "No detailed measurements saved", "reported", at, false));
            }
        }

        var patterns = new List<RecurringPatternDto>();
        foreach (var group in initial.Where(r => Warning(r.Status) || !r.Measured).GroupBy(r => r.Key))
        {
            var occurrences = group.GroupBy(r => r.Session.Id).Select(g => g.First()).OrderBy(r => r.At).ToArray();
            if (occurrences.Length < 2) continue;
            var first = occurrences[0];
            var last = occurrences[^1];
            var recentRows = latest.Where(r => r.Key == group.Key).OrderByDescending(r => r.At).ToArray();
            var recent = recentRows.FirstOrDefault();
            var sameTime = recentRows.Where(r => r.At == recent?.At).ToArray();
            var latestStatus = sameTime.Any(r => Warning(r.Status)) ? "high"
                : sameTime.Length > 0 && sameTime.All(r => Normal(r.Status) && !string.IsNullOrWhiteSpace(r.Value) &&
                    !r.Value.Contains("unavailable", StringComparison.OrdinalIgnoreCase) && !r.Value.Contains("not verified", StringComparison.OrdinalIgnoreCase))
                    ? "normal" : "unknown";
            var current = recent == null ? "Not verified"
                : recent.Session.ResolutionStatus == "needs_recheck" && recent.Session.ResolutionProof.Length == 0
                    ? "Action finished; recheck needed" : Warning(latestStatus) ? "Warning in latest saved check"
                : Normal(latestStatus) ? "Not detected in latest saved check" : "Could not verify in latest saved check";
            var risk = Risk(group.Key, last.Measured);
            if (recent?.FollowUpEvents == true && group.Key is "application_crash_history" or "system_crash_history")
                current = Normal(latestStatus) ? "No new matching records in latest follow-up"
                    : Warning(latestStatus) ? "Matching records in latest follow-up" : "Follow-up records not verified";
            var evidence = last.Measured
                ? $"The same type of warning was recorded in {occurrences.Length} separate saved checks. Latest warning: {last.Value}. These may be observations of one continuing condition; they do not prove separate failures or a faulty part."
                : $"The same concern was reported in {occurrences.Length} saved checks, but detailed measurements are missing. The cause and current condition are not verified.";
            var pattern = new RecurringPatternDto
            {
                id = "RP-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)))[..12],
                pattern_key = group.Key, symptom = last.Label, probable_cause = last.Label,
                occurrence_count = occurrences.Length, first_detected = Date(first.At), latest_detected = Date(last.At),
                previous_action = NormalizeActionCategory(first.Session.ActionCategory),
                updated_action = NormalizeActionCategory(last.Session.ActionCategory),
                status = "Repeated observations", current_condition = current,
                risk_level = risk.Level, priority_rank = risk.Rank, risk_reason = risk.Reason,
                evidence_summary = evidence, action_escalated = false,
                recommended_next_step = NextStep(group.Key, current, last.Measured),
                occurrences = occurrences.Select((r, index) => new PatternOccurrenceDto
                {
                    id = r.Session.Id.ToString(), session_code = $"S-{index + 1:000}",
                    display_date = Date(r.At), short_date = r.At == DateTime.MinValue ? "Unknown" : r.At.ToString("MMM dd"),
                    display_time = r.At == DateTime.MinValue ? "Unknown" : r.At.ToString("hh:mm tt"),
                    symptom = r.Label, probable_cause = r.Label + ": " + r.Value,
                    action_category = NormalizeActionCategory(r.Session.ActionCategory),
                    confidence_label = r.EvidenceKind is "measurement" or "reported_status" or "event_log" ? "Available" : "NotRecorded",
                    severity = r.Session.Severity, frequency = r.Session.Frequency, duration = r.Session.Duration,
                    warning_signs = string.IsNullOrWhiteSpace(r.Session.WarningSigns) ? [] : [r.Session.WarningSigns],
                    status = "Recorded warning"
                }).ToList()
            };
            pattern.timeline = pattern.occurrences.Select(o => new PatternTimelineDotDto
                { date = o.short_date, full_date = o.display_date, session_id = o.id }).ToList();
            patterns.Add(pattern);
        }
        patterns = patterns.OrderByDescending(p => p.priority_rank)
            .ThenByDescending(p => p.occurrence_count).ThenBy(p => p.pattern_key, StringComparer.Ordinal).ToList();
        return new RecurringResponseDto
        {
            patterns = patterns,
            metrics = new RecurringMetricsDto
            {
                recurring_issues = patterns.Count,
                // Advice escalation is not evidence of a worsening device.
                worsening_trends = 0, action_escalated = 0,
                total_occurrences = patterns.SelectMany(p => p.occurrences).Select(o => o.id).Distinct().Count()
            },
            timeline = patterns.SelectMany(p => p.occurrences.Select(o => new RecurringTimelineRowDto
            {
                pattern_id = p.id, date = o.display_date, symptom = o.symptom, probable_cause = o.probable_cause,
                action_category = o.action_category, confidence_label = o.confidence_label, status = o.status
            })).ToList()
        };
    }

    private static bool IsLegacyConcern(RecurringSessionDto s)
    {
        var category = s.DiagnosedCategory.Trim().ToLowerInvariant();
        return category.Length > 0 && !category.Contains("no active issue") && !category.Contains("no issue") &&
            !category.Contains("unavailable") && !category.Contains("not present") &&
            !category.Contains("could not verify") && !category.Contains("normal") &&
            (s.ActionCategory.Equals("Troubleshoot", StringComparison.OrdinalIgnoreCase) ||
             s.ActionCategory.Equals("Escalate", StringComparison.OrdinalIgnoreCase) ||
             s.ActionCategory.Equals("Repair", StringComparison.OrdinalIgnoreCase));
    }

    private static (string Level, int Rank, string Reason) Risk(string key, bool measured) => !measured
        ? ("Needs verification", 0, "Saved reports do not establish the level of device risk.")
        : key == "storage_health"
            ? ("Protect files first", 3, "A reported drive-health warning can put files at risk. Frequency alone does not determine priority.")
        : key is "cpu_temperature" or "gpu_temperature" or "cpu_throttling" or "system_crash_history"
            ? ("Review promptly", 2, "Recorded heat or system-stability warnings can interrupt work. They do not identify a failed part.")
        : ("Review", 1, "A repeated workload, connection or device warning needs review, but does not by itself prove hardware damage.");

    private static string NextStep(string key, string current, bool measured)
    {
        if (!measured) return "Open the related checks and run the relevant check again. Do not repair or replace a part based on these reports alone.";
        if (current.StartsWith("Not detected", StringComparison.Ordinal) && key != "storage_health")
            return "The latest saved reading did not show this warning. No action is indicated by this reading. Keep the history and recheck if symptoms return; this does not establish overall hardware health.";
        return key switch
        {
            "storage_health" => "Back up important files and arrange a drive-health inspection. File cleanup cannot repair a physically failing drive.",
            "cpu_temperature" or "gpu_temperature" or "cpu_throttling" => "Save your work and reduce heavy activity. Check ventilation; arrange hardware inspection if heat or shutdowns continue. Software cannot repair a broken fan.",
            "system_crash_history" => "Save your work and review the recorded restart or stop error. Use the report to investigate software, drivers and hardware; a restart record alone does not identify the cause.",
            "application_crash_history" => "Review the affected app, event times and error details. No new records in a short follow-up do not prove a repair. If symptoms return, follow the app maker's troubleshooting guidance.",
            "memory_usage" or "memory_app_warning" => "Save your work, review memory-heavy apps, and approve closing only apps you no longer need. Recheck memory afterward. High use is not a memory-cell test.",
            "cpu_usage" => "Review the apps using the processor. Recheck after the workload settles; a brief high reading does not prove a processor fault.",
            "storage_usage" => "Review free storage space. Preview any cleanup, approve only the files you intend to remove, and check space afterward.",
            "battery_charge" => "Connect a suitable charger and check whether charging starts. Repeated low charge alone does not measure battery wear.",
            _ when key.StartsWith("network_", StringComparison.Ordinal) => "Check the connection and which network test failed. Approve only a relevant network action, then repeat the same test.",
            _ => "Open the related checks to identify the affected device or app. Review a relevant action before approving it; seek a technician if the problem continues or physical damage is suspected."
        };
    }

    public string NormalizeActionCategory(string? action)
    {
        var value = (action ?? "").ToLowerInvariant();
        return value.Contains("escalate") || value.Contains("professional") ? "Escalate"
            : value.Contains("troubleshoot") || value.Contains("repair") ? "Troubleshoot"
            : value.Contains("maintain") ? "Maintain" : "Monitor";
    }

    private static string Date(DateTime at) => at == DateTime.MinValue ? "Unknown date" : at.ToString("MMM dd, yyyy");
}
