using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class QueryWindowsEventLogsTool : IRigMdAgentTool
{
    private readonly Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Output, string Error)>> _runQuery;

    public QueryWindowsEventLogsTool() : this(RunQueryAsync) { }

    public QueryWindowsEventLogsTool(
        Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Output, string Error)>> runQuery)
    {
        _runQuery = runQuery ?? throw new ArgumentNullException(nameof(runQuery));
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunQueryAsync(
        ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows event query could not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }

    public string Name => "query_windows_event_logs";

    public string DisplayName => "Query Windows Event Logs (System & Application)";

    public string Description =>
        "Queries recent Critical (Level 1), Error (Level 2), and Warning (Level 3) entries from the Windows 'System' or 'Application' Event Log to uncover driver crashes, disk/NTFS errors, service failures, or application faults.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["logName"] = new()
                {
                    Type = "string",
                    Description = "Windows Event Log channel to query: 'System' or 'Application' (default: 'System').",
                    EnumValues = new List<string> { "System", "Application" }
                },
                ["maxEvents"] = new()
                {
                    Type = "integer",
                    Description = "Maximum number of recent error/warning events to retrieve (default: 12, max: 25)."
                },
                ["hoursBack"] = new()
                {
                    Type = "integer",
                    Description = "Time window in hours to search backwards from now (default: 48, max: 168)."
                },
                ["sinceUtc"] = new()
                {
                    Type = "string",
                    Description = "Optional ISO 8601 timestamp with a UTC offset. Only records written at or after this time are included."
                },
                ["eventFocus"] = new()
                {
                    Type = "string",
                    Description = "Optional event filter focus: 'General' (default), 'ApplicationCrash' (Application Error / Windows Error Reporting / .NET Runtime faults), 'Boot' (Event 100 / boot performance), or 'BugCheck' (Kernel-Power 41 / BugCheck 1001 / unexpected shutdown 6008).",
                    EnumValues = new List<string> { "General", "ApplicationCrash", "Boot", "BugCheck" }
                }
            },
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = false,
            WhatWillHappen = "Queries recent Error/Warning records from the Windows Event Log in read-only mode."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var rawLogName = ToolArgumentHelper.GetString(arguments, "logName", "System");
        var eventFocus = ToolArgumentHelper.GetString(arguments, "eventFocus", "General");
        var normalizedFocus = eventFocus.Trim();
        var logName = string.Equals(normalizedFocus, "ApplicationCrash", StringComparison.OrdinalIgnoreCase)
            ? "Application"
            : string.Equals(rawLogName, "Application", StringComparison.OrdinalIgnoreCase)
            ? "Application"
            : "System";

        if (string.Equals(normalizedFocus, "BugCheck", StringComparison.OrdinalIgnoreCase)) logName = "System";
        var sinceText = ToolArgumentHelper.GetString(arguments, "sinceUtc", "");
        DateTimeOffset? since = null;
        if (!string.IsNullOrWhiteSpace(sinceText))
        {
            if (!(sinceText.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                  System.Text.RegularExpressions.Regex.IsMatch(sinceText, @"[+-]\d{2}:\d{2}$")) ||
                !DateTimeOffset.TryParse(sinceText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
                parsed > DateTimeOffset.UtcNow)
                return new AgentToolExecutionResult
                {
                    ToolName = Name, Success = false, Summary = "The event-check start time is invalid.",
                    DataJson = JsonSerializer.Serialize(new { logName, eventFocus = normalizedFocus, eventCount = (int?)null, warning = "Invalid start time." })
                };
            since = parsed.ToUniversalTime();
        }

        var maxEvents = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "maxEvents", 12), 1, 25);
        var hoursBack = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "hoursBack", 48), 1, 168);

        progressReporter?.Invoke($"Querying Windows '{logName}' Event Log (focus: {eventFocus}, last {hoursBack}h, up to {maxEvents} events)...");

        var timeDiffMs = (long)TimeSpan.FromHours(hoursBack).TotalMilliseconds;
        var timeFilter = since.HasValue
            ? $"TimeCreated[@SystemTime >= '{since.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}']"
            : $"TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]";
        // Match the selected record family, not every error in the same channel.
        var focusFilter = normalizedFocus.ToLowerInvariant() switch
        {
            "bugcheck" => "((EventID=41 and Provider[@Name='Microsoft-Windows-Kernel-Power']) or (EventID=1001 and (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck'])) or (EventID=6008 and Provider[@Name='EventLog']))",
            "applicationcrash" => "((EventID=1000 and Provider[@Name='Application Error']) or (EventID=1001 and Provider[@Name='Windows Error Reporting']) or (EventID=1002 and Provider[@Name='Application Hang']) or (EventID=1026 and Provider[@Name='.NET Runtime']))",
            "boot" => "(EventID=100 or EventID=6005 or EventID=6006 or EventID=7000 or EventID=7001 or Level=1 or Level=2 or Level=3)",
            _ => "(Level=1 or Level=2 or Level=3)"
        };
        var xpathQuery = $"*[System[{focusFilter} and {timeFilter}]]";

        var events = new List<object>();
        string? queryWarning = null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wevtutil.exe",
                Arguments = $"qe {logName} /c:{maxEvents} /rd:true /f:RenderedXml /q:\"{xpathQuery}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var response = await _runQuery(psi, cancellationToken);
            var stdout = response.Output;
            var stderr = response.Error;
            if (response.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
                queryWarning = string.IsNullOrWhiteSpace(stderr)
                    ? $"Windows event query failed (exit code {response.ExitCode})." : stderr.Trim();
            {
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    var wrappedXml = $"<Events>{stdout}</Events>";
                    var doc = XDocument.Parse(wrappedXml);
                    XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
                    if (doc.Root?.Elements(ns + "Event").Any() != true)
                        queryWarning = "Windows event query returned an unrecognized response.";

                    foreach (var ev in doc.Root?.Elements(ns + "Event") ?? Enumerable.Empty<XElement>())
                    {
                        var sys = ev.Element(ns + "System");
                        var renderingInfo = ev.Element(ns + "RenderingInfo");

                        var providerName = sys?.Element(ns + "Provider")?.Attribute("Name")?.Value ?? "Unknown";
                        var eventId = sys?.Element(ns + "EventID")?.Value ?? "0";
                        if (!MatchesFocus(normalizedFocus, providerName, eventId)) continue;
                        var levelCode = sys?.Element(ns + "Level")?.Value ?? "0";
                        var timeCreated = sys?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value ?? "";
                        if (since.HasValue)
                        {
                            if (!DateTimeOffset.TryParse(timeCreated, CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordedAt))
                            {
                                queryWarning = "A returned event has no valid recording time.";
                                continue;
                            }
                            if (recordedAt < since.Value) continue;
                            if (recordedAt > DateTimeOffset.UtcNow)
                            {
                                queryWarning = "A returned event has a future recording time.";
                                continue;
                            }
                        }
                        var message = renderingInfo?.Element(ns + "Message")?.Value;

                        if (string.IsNullOrWhiteSpace(message))
                        {
                            var dataItems = ev.Element(ns + "EventData")?
                                .Elements(ns + "Data")
                                .Select(d => d.Value)
                                .Where(v => !string.IsNullOrWhiteSpace(v))
                                .Take(4)
                                .ToList();

                            if (dataItems != null && dataItems.Count > 0)
                            {
                                message = string.Join(" | ", dataItems);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(message) && message.Length > 320)
                        {
                            message = message.Substring(0, 320) + "...";
                        }

                        var levelLabel = levelCode switch
                        {
                            "1" => "Critical",
                            "2" => "Error",
                            "3" => "Warning",
                            _ => $"Level {levelCode}"
                        };

                        events.Add(new
                        {
                            timeUtc = timeCreated,
                            level = levelLabel,
                            provider = providerName,
                            eventId,
                            message = message ?? "(No rendered message)"
                        });
                    }
                }
                else if (!string.IsNullOrWhiteSpace(stderr))
                {
                    queryWarning = stderr.Trim();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            queryWarning = ex.Message;
        }

        var payload = new
        {
            logName,
            eventFocus = normalizedFocus,
            hoursBack,
            sinceUtc = since?.ToString("O", CultureInfo.InvariantCulture),
            checkedAtUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            eventCount = queryWarning == null ? (int?)events.Count : null,
            warning = queryWarning,
            events
        };

        var focusLabel = string.Equals(normalizedFocus, "ApplicationCrash", StringComparison.OrdinalIgnoreCase)
            ? "application crash/fault"
            : string.Equals(normalizedFocus, "BugCheck", StringComparison.OrdinalIgnoreCase)
                ? "system crash/stop-error"
                : string.Equals(normalizedFocus, "Boot", StringComparison.OrdinalIgnoreCase)
                    ? "boot/startup"
                    : "Critical/Error/Warning";

        var window = since.HasValue ? $"since {since.Value:O}" : $"last {hoursBack}h";
        var summary = queryWarning == null
            ? $"Retrieved {events.Count} matching {focusLabel} record(s) from the Windows '{logName}' log ({window})."
            : "Windows error records could not be checked. No absence of errors can be confirmed.";

        return new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = queryWarning == null,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        };
    }

    private static bool MatchesFocus(string focus, string provider, string eventId)
    {
        bool Is(string name) => string.Equals(provider, name, StringComparison.OrdinalIgnoreCase);
        return focus.ToLowerInvariant() switch
        {
            "applicationcrash" => (eventId == "1000" && Is("Application Error")) ||
                (eventId == "1001" && Is("Windows Error Reporting")) ||
                (eventId == "1002" && Is("Application Hang")) || (eventId == "1026" && Is(".NET Runtime")),
            "bugcheck" => (eventId == "41" && Is("Microsoft-Windows-Kernel-Power")) ||
                (eventId == "1001" && (Is("Microsoft-Windows-WER-SystemErrorReporting") || Is("BugCheck"))) ||
                (eventId == "6008" && Is("EventLog")),
            _ => true
        };
    }
}
