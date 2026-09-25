using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class QueryWindowsEventLogsTool : IRigMdAgentTool
{
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
                ["eventFocus"] = new()
                {
                    Type = "string",
                    Description = "Optional event filter focus: 'General' (default), 'Boot' (Event 100 / boot performance), or 'BugCheck' (Kernel-Power 41 / BugCheck 1001 / unexpected shutdown 6008).",
                    EnumValues = new List<string> { "General", "Boot", "BugCheck" }
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
        var logName = string.Equals(rawLogName, "Application", StringComparison.OrdinalIgnoreCase)
            ? "Application"
            : "System";

        var maxEvents = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "maxEvents", 12), 1, 25);
        var hoursBack = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "hoursBack", 48), 1, 168);

        progressReporter?.Invoke($"Querying Windows '{logName}' Event Log (focus: {eventFocus}, last {hoursBack}h, up to {maxEvents} events)...");

        var timeDiffMs = (long)TimeSpan.FromHours(hoursBack).TotalMilliseconds;
        var xpathQuery = string.Equals(eventFocus, "BugCheck", StringComparison.OrdinalIgnoreCase)
            ? $"*[System[(EventID=41 or EventID=1001 or EventID=6008 or Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]]]"
            : string.Equals(eventFocus, "Boot", StringComparison.OrdinalIgnoreCase)
                ? $"*[System[(EventID=100 or EventID=6005 or EventID=6006 or EventID=7000 or EventID=7001 or Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]]]"
                : $"*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= {timeDiffMs}]]]";

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

            using var process = Process.Start(psi);
            if (process != null)
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    var wrappedXml = $"<Events>{stdout}</Events>";
                    var doc = XDocument.Parse(wrappedXml);
                    XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";

                    foreach (var ev in doc.Root?.Elements(ns + "Event") ?? Enumerable.Empty<XElement>())
                    {
                        var sys = ev.Element(ns + "System");
                        var renderingInfo = ev.Element(ns + "RenderingInfo");

                        var providerName = sys?.Element(ns + "Provider")?.Attribute("Name")?.Value ?? "Unknown";
                        var eventId = sys?.Element(ns + "EventID")?.Value ?? "0";
                        var levelCode = sys?.Element(ns + "Level")?.Value ?? "0";
                        var timeCreated = sys?.Element(ns + "TimeCreated")?.Attribute("SystemTime")?.Value ?? "";
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
        catch (Exception ex)
        {
            queryWarning = ex.Message;
        }

        var payload = new
        {
            logName,
            hoursBack,
            eventCount = events.Count,
            warning = queryWarning,
            events
        };

        var summary =
            $"Retrieved {events.Count} recent Critical/Error/Warning event(s) from the Windows '{logName}' log (last {hoursBack}h).";

        return new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        };
    }
}
