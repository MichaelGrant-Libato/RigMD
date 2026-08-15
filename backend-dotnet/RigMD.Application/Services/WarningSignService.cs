using RigMD.Application.Models;

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
        }
    };

    public WarningSignsResponseDto BuildReference(
        IEnumerable<string> observedTexts,
        string category = "all",
        string search = "",
        bool observedOnly = false)
    {
        var observedList =
            observedTexts
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

        var categoryFilter =
            NormalizeText(category);

        var searchFilter =
            NormalizeText(search);

        var rows =
            new List<WarningSignRowDto>();

        foreach (var reference in References)
        {
            var observedCount =
                GetObservedCount(
                    reference,
                    observedList
                );

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

    private static int GetObservedCount(
        WarningReference reference,
        IEnumerable<string> observedTexts)
    {
        var warningName =
            NormalizeText(
                reference.WarningSign
            );

        var keywords =
            reference.Keywords
                .Select(NormalizeText)
                .ToArray();

        var count = 0;

        foreach (var observed in observedTexts)
        {
            var observedValue =
                NormalizeText(observed);

            if (string.IsNullOrWhiteSpace(observedValue))
                continue;

            if (
                warningName.Contains(observedValue) ||
                observedValue.Contains(warningName)
            )
            {
                count++;
                continue;
            }

            if (
                keywords.Any(
                    keyword =>
                        !string.IsNullOrWhiteSpace(keyword) &&
                        observedValue.Contains(keyword)
                )
            )
            {
                count++;
            }
        }

        return count;
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
            );
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