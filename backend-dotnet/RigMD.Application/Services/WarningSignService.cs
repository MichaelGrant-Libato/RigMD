using RigMD.Application.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RigMD.Application.Services;

public class WarningSignService
{
    private sealed class WarningReference
    {
        public string Id { get; init; } = string.Empty;
        public string WarningSign { get; init; } = string.Empty;
        public string Meaning { get; init; } = string.Empty;
        public string Threshold { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string[] Keywords { get; init; } = Array.Empty<string>();
    }

    private static readonly WarningReference[] References =
    {
        new()
        {
            Id = "WS-001",
            WarningSign = "Stuttering or Freezing",
            Meaning =
                "The system is struggling to keep up with active work, often because of RAM pressure, CPU load, browser load, or background apps.",
            Threshold =
                "Repeated cursor freezes, app hangs, short system pauses, or noticeable stuttering during normal use.",
            Action = "Maintain",
            Category = "Performance",
            Keywords =
            [
                "stuttering",
                "stutter",
                "freeze",
                "freezing",
                "lag",
                "slow",
                "hitch",
                "hang",
                "unresponsive"
            ]
        },

        new()
        {
            Id = "WS-002",
            WarningSign = "Blue Screen or Crash Code",
            Meaning =
                "A Windows crash may indicate driver conflict, memory instability, or system-level failure.",
            Threshold =
                "Any repeated blue screen, restart loop, or visible stop code.",
            Action = "Troubleshoot",
            Category = "Crash / Stability",
            Keywords =
            [
                "blue screen",
                "bsod",
                "stop code",
                "crash",
                "restart",
                "critical crash"
            ]
        },

        new()
        {
            Id = "WS-003",
            WarningSign = "Display Driver or Visual Glitch",
            Meaning =
                "Graphics driver instability may cause flicker, black screens, artifacts, or crashes during visual workloads.",
            Threshold =
                "Repeated flickering, black screen recovery, visual artifacts, or display-driver warnings.",
            Action = "Troubleshoot",
            Category = "Drivers / Display",
            Keywords =
            [
                "display driver",
                "flicker",
                "screen flicker",
                "black screen",
                "visual",
                "artifact",
                "glitch",
                "screen tearing"
            ]
        },

        new()
        {
            Id = "WS-004",
            WarningSign = "No Display",
            Meaning =
                "The device may be failing before Windows fully loads, or the display path may have a hardware-level issue.",
            Threshold =
                "Power is on but the screen remains black or no signal appears.",
            Action = "Escalate",
            Category = "Boot / Display",
            Keywords =
            [
                "no display",
                "black screen",
                "no signal"
            ]
        },

        new()
        {
            Id = "WS-005",
            WarningSign = "No Boot Device or Startup Repair",
            Meaning =
                "Windows may not be reaching the operating system because of boot configuration, storage, or system file issues.",
            Threshold =
                "No boot device message, startup repair loop, or repeated failure before desktop.",
            Action = "Troubleshoot",
            Category = "Boot",
            Keywords =
            [
                "no boot device",
                "startup repair",
                "boot loop",
                "automatic repair",
                "won't start",
                "startup"
            ]
        },

        new()
        {
            Id = "WS-006",
            WarningSign = "Storage SMART Caution or File Access Error",
            Meaning =
                "The storage device may be reporting health warnings or file access failures. Backups should be prioritized before deeper repair steps.",
            Threshold =
                "SMART caution, bad status, file access errors, or repeated save/open failures.",
            Action = "Escalate",
            Category = "Storage",
            Keywords =
            [
                "smart",
                "caution",
                "bad",
                "file not found",
                "access denied",
                "save error",
                "storage warning",
                "disk health"
            ]
        },

        new()
        {
            Id = "WS-007",
            WarningSign = "Loud Fan Noise or Heat",
            Meaning =
                "The system may be reacting to heat or sustained workload. This is common during gaming or heavy multitasking.",
            Threshold =
                "Fans suddenly become loud, the device feels hot, or performance drops during heavy use.",
            Action = "Maintain",
            Category = "Thermal",
            Keywords =
            [
                "loud fan",
                "fan noise",
                "overheat",
                "overheating",
                "hot",
                "thermal",
                "throttling"
            ]
        },

        new()
        {
            Id = "WS-008",
            WarningSign = "Windows Update Failure",
            Meaning =
                "Repeated Windows update failures may point to OS corruption, storage issues, or service errors.",
            Threshold =
                "Two or more failed update attempts.",
            Action = "Troubleshoot",
            Category = "OS",
            Keywords =
            [
                "windows update",
                "update failed",
                "update failure"
            ]
        },

        new()
        {
            Id = "WS-009",
            WarningSign = "Application Crashes",
            Meaning =
                "One or more applications are crashing repeatedly during normal use.",
            Threshold =
                "Repeated app crashes in the same session or across multiple sessions.",
            Action = "Troubleshoot",
            Category = "Software",
            Keywords =
            [
                "app crash",
                "application crash",
                "program crash",
                "crashes",
                "error message"
            ]
        },

        new()
        {
            Id = "WS-010",
            WarningSign = "Slow Boot or Startup Delay",
            Meaning =
                "Startup delay may indicate storage pressure, startup app load, driver delays, or OS-level performance issues.",
            Threshold =
                "Boot time feels unusually long or exceeds about 60 seconds.",
            Action = "Maintain",
            Category = "Boot",
            Keywords =
            [
                "slow boot",
                "boot time",
                "startup delay",
                "startup slow"
            ]
        },
        new()
        {
            Id = "WS-011", WarningSign = "High Memory Use", Category = "Memory", Action = "Maintain",
            Meaning = "A saved check flagged physical memory use for review. This does not establish a memory leak or faulty RAM.",
            Threshold = "A supported memory-use reading was saved with a warning status.",
            Keywords = ["memory usage", "RAM usage"]
        },
        new()
        {
            Id = "WS-012", WarningSign = "High Processor Activity", Category = "Performance", Action = "Monitor",
            Meaning = "A saved check flagged processor activity. High activity can be expected for the workload and does not establish processor damage.",
            Threshold = "A supported processor-activity reading was saved with a warning status.",
            Keywords = ["CPU usage", "processor activity"]
        },
        new()
        {
            Id = "WS-013", WarningSign = "Elevated Storage Space Use", Category = "Storage", Action = "Maintain",
            Meaning = "A saved check flagged used storage capacity. Space use is not drive health, disk activity or proof of drive failure.",
            Threshold = "A supported storage-capacity reading was saved with a warning status.",
            Keywords = ["storage space", "disk capacity"]
        },
        new()
        {
            Id = "WS-014", WarningSign = "Network Check Warning", Category = "Network", Action = "Troubleshoot",
            Meaning = "A saved network check flagged configuration, lookup, signal or connection-test evidence. One failed test does not establish an internet outage.",
            Threshold = "A supported network reading was saved with a warning status.",
            Keywords = ["network", "DNS", "latency", "packet loss"]
        },
        new()
        {
            Id = "WS-015", WarningSign = "Device Manager Error Code", Category = "Devices / Drivers", Action = "Troubleshoot",
            Meaning = "A saved check recorded a device status error. Review the exact code; it does not by itself establish a driver conflict or hardware fault.",
            Threshold = "A supported device-error reading was saved with a warning status.",
            Keywords = ["device status", "Device Manager"]
        },
        new()
        {
            Id = "WS-016", WarningSign = "Application Memory Warning", Category = "Memory", Action = "Troubleshoot",
            Meaning = "A saved process inspection reported unusual application memory behavior. This is separate from total physical memory use and does not confirm a leak.",
            Threshold = "An application-memory warning was saved by a supported check.",
            Keywords = ["application memory", "process memory"]
        },
        new()
        {
            Id = "WS-017", WarningSign = "Low Battery Charge", Category = "Power", Action = "Maintain",
            Meaning = "A saved check flagged battery charge. Charge alone does not establish battery wear, rapid drain or a faulty battery.",
            Threshold = "A supported battery-charge reading was saved with a warning status.",
            Keywords = ["battery charge", "power"]
        },
        new()
        {
            Id = "WS-018", WarningSign = "System Crash or Unexpected Restart Records", Category = "Crash / Stability", Action = "Troubleshoot",
            Meaning = "A saved event-log check found system crash or unexpected restart records. These do not establish a blue screen, a cause or a fault happening now.",
            Threshold = "A supported system-event history check was saved with a warning status.",
            Keywords = ["system event", "crash history", "unexpected restart"]
        }
    };

    public WarningSignsResponseDto BuildReference(
        IEnumerable<string> observedTexts,
        string category = "all",
        string search = "",
        bool observedOnly = false)
    {
        // Each entry represents one saved session. Repeated text in different sessions
        // must remain separate; multiple references/aliases in one session count once.
        var observedSessions = observedTexts.Select(GetLegacyReferenceIds).ToArray();

        var categoryFilter =
            NormalizeText(category);

        var searchFilter =
            NormalizeText(search);

        var rows =
            new List<WarningSignRowDto>();

        foreach (var reference in References)
        {
            var observedCount = observedSessions.Count(ids => ids.Contains(reference.Id));

            var row =
                new WarningSignRowDto
                {
                    id = reference.Id,
                    warning_sign = reference.WarningSign,
                    meaning = reference.Meaning,
                    threshold = reference.Threshold,
                    action = reference.Action,
                    category = reference.Category,
                    keywords = reference.Keywords,
                    observed = observedCount > 0,
                    observed_count = observedCount
                };

            var categoryMatches =
                string.IsNullOrWhiteSpace(categoryFilter) ||
                categoryFilter == "all" ||
                NormalizeText(row.category) == categoryFilter;

            var searchableText =
                string.Join(
                    " ",
                    row.warning_sign,
                    row.meaning,
                    row.threshold,
                    row.action,
                    row.category,
                    string.Join(" ", row.keywords)
                )
                .ToLowerInvariant();

            var searchMatches =
                string.IsNullOrWhiteSpace(searchFilter) ||
                searchableText.Contains(searchFilter);

            var observedMatches =
                !observedOnly ||
                row.observed;

            if (
                categoryMatches &&
                searchMatches &&
                observedMatches
            )
            {
                rows.Add(row);
            }
        }

        return new WarningSignsResponseDto
        {
            summary =
                new WarningSummaryDto
                {
                    observed_warning_signs =
                        rows.Count(row => row.observed),

                    total_observed_occurrences =
                        rows.Sum(row => row.observed_count),

                    total_reference_items =
                        References.Length
                },

            categories =
                References
                    .Select(reference => reference.Category)
                    .Distinct()
                    .OrderBy(categoryName => categoryName)
                    .ToArray(),

            warning_signs =
                rows
        };
    }

    public WarningSignsResponseDto EmptyResponse(
        string? warning = null)
    {
        var response =
            BuildReference(
                Array.Empty<string>()
            );

        response.database_warning =
            warning;

        return response;
    }

    public int GetObservedReferenceCount(
        IEnumerable<string> observedTexts)
    {
        return BuildReference(
            observedTexts
        )
        .summary
        .observed_warning_signs;
    }

    public List<WarningSignRowDto> GetObservedReferenceRows(
        IEnumerable<string> observedTexts)
    {
        return BuildReference(
            observedTexts,
            "all",
            "",
            true
        )
        .warning_signs;
    }

    /// <summary>
    /// Historical warning references for one saved check, never its current resolution.
    /// Read original proof so existing automatic checks work without a backfill/write.
    /// Labels, meanings, diagnosis text and search keywords are not warning evidence.
    /// </summary>
    public static string[] GetSessionReferenceIds(string? legacyWarningSigns, string? savedProof)
    {
        var ids = GetLegacyReferenceIds(legacyWarningSigns);
        if (!string.IsNullOrWhiteSpace(savedProof))
        {
            try
            {
                using var document = JsonDocument.Parse(savedProof);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var status = NormalizeText(ReadString(item, "status", "Status"));
                        if (status is not ("high" or "elevated" or "warning" or "critical" or "still needs attention")) continue;
                        var kind = NormalizeText(ReadString(item, "evidence_kind", "evidenceKind", "EvidenceKind"));
                        // Older saved checks may lack evidence_kind. Explicit unavailable
                        // or contextual evidence must never become a warning.
                        if (kind.Length > 0 && kind is not ("measurement" or "event_log" or "reported_status")) continue;
                        var id = ReferenceForMetric(NormalizeText(ReadString(item, "metric_id", "metricId", "MetricId")));
                        if (id != null) ids.Add(id);
                    }
                }
            }
            catch (JsonException)
            {
                // Damaged/legacy proof cannot justify a warning; known legacy entries still can.
            }
        }
        return References.Where(reference => ids.Contains(reference.Id)).Select(reference => reference.Id).ToArray();
    }

    private static string? ReferenceForMetric(string metric) => metric switch
    {
        "memory_usage" => "WS-011",
        "cpu_usage" => "WS-012",
        "storage_usage" => "WS-013",
        "storage_health" => "WS-006",
        "cpu_temperature" or "gpu_temperature" or "cpu_throttling" => "WS-007",
        "application_crash_history" => "WS-009",
        "system_crash_history" => "WS-018",
        "memory_app_warning" => "WS-016",
        "battery_charge" => "WS-017",
        "network_connectivity" or "network_adapter" or "network_address" or "network_gateway" or
            "network_dns_config" or "network_dns" or "network_latency" or "network_loss" or "network_signal" => "WS-014",
        _ when Regex.IsMatch(metric, @"^storage_usage_[a-z]:\\?$", RegexOptions.CultureInvariant) => "WS-013",
        // Numeric IDs were used by older checks; identity is not used here to infer
        // a continuing fault, only to record that the check reported a device error.
        _ when Regex.IsMatch(metric, @"^device_error_(?:[0-9a-f]{24}|[0-9]+)$", RegexOptions.CultureInvariant) => "WS-015",
        _ => null
    };

    private static string ReadString(JsonElement item, params string[] names)
    {
        foreach (var name in names)
            if (item.TryGetProperty(name, out var value))
                return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        return "";
    }

    private static HashSet<string> GetLegacyReferenceIds(string? raw)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in SplitWarningSigns(raw))
        {
            var phrase = Regex.Replace(NormalizeText(value).TrimEnd('.', '!'), @"\s+", " ");
            // Exact names/IDs and an explicit phrase allowlist only. In particular,
            // "No blue screen", "SMART OK", "startup delay" and arbitrary prose
            // cannot acquire extra warnings through substring/keyword matching.
            var known = References.FirstOrDefault(reference =>
                NormalizeText(reference.Id) == phrase || NormalizeText(reference.WarningSign) == phrase);
            var id = known?.Id ?? (phrase switch
            {
                "stuttering" or "stutter" or "freezing" or "system is stuttering" or "system is freezing" or
                    "system freezing" or "slow system" or "app hangs" or "system hangs" => "WS-001",
                "blue screen" or "bsod" or "stop code" or "blue screen crash" or "system crash" or
                    "unexpected restart" or "critical crash" => "WS-002",
                "display driver warning" or "display driver error" or "screen flicker" or "screen flickering" or
                    "flickering" or "visual artifacts" or "screen tearing" or "black screen recovery" or "visual glitch" => "WS-003",
                "no signal" => "WS-004",
                "no boot device" or "startup repair" or "startup repair loop" or "boot loop" or
                    "automatic repair" or "automatic repair loop" or "won't start" => "WS-005",
                "smart caution" or "smart warning" or "smart failure" or "smart failed" or "smart bad" or
                    "storage warning" or "disk health warning" or "file access error" or "file access errors" or
                    "access denied" or "save error" or "file not found" => "WS-006",
                "loud fan" or "loud fan noise" or "fan noise" or "overheat" or "overheating" or
                    "thermal throttling" or "high temperature" => "WS-007",
                "windows update failed" or "update failed" or "update failure" => "WS-008",
                "app crash" or "application crash" or "program crash" or "app crashes" or "program crashes" => "WS-009",
                "slow boot" or "startup delay" or "startup slow" => "WS-010",
                _ => null
            });
            if (id != null) ids.Add(id);
        }
        return ids;
    }

    public static string[] SplitWarningSigns(
        string? rawWarningSigns)
    {
        if (string.IsNullOrWhiteSpace(rawWarningSigns))
            return Array.Empty<string>();

        return rawWarningSigns
            .Replace(";", "\n")
            .Replace(",", "\n")
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeText(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value
                .Trim()
                .ToLowerInvariant();
    }
}
