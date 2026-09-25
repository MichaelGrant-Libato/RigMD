using System;
using System.Collections.Generic;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class QueryStartupAppsTool : IRigMdAgentTool
{
    public string Name => "query_startup_apps";

    public string DisplayName => "Query Windows Startup Applications";

    public string Description =>
        "Enumerates applications and commands configured to launch automatically at Windows startup via WMI (Win32_StartupCommand) and Windows Run registry keys.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>(),
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
            WhatWillHappen = "Reads Windows startup entries from WMI Win32_StartupCommand and Run registry keys in read-only mode."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Querying Windows startup applications and autorun entries...");

        var startupItems = new List<object>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Command, Location, User FROM Win32_StartupCommand");
            foreach (var obj in searcher.Get())
            {
                var name = (obj["Name"]?.ToString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name) || !seenNames.Add(name))
                {
                    continue;
                }

                startupItems.Add(new
                {
                    name,
                    command = obj["Command"]?.ToString() ?? string.Empty,
                    location = obj["Location"]?.ToString() ?? "WMI Startup",
                    user = obj["User"]?.ToString() ?? "Current User"
                });
            }
        }
        catch
        {
            // Ignore WMI query errors and fall back to registry inspection
        }

        ReadRunKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", seenNames, startupItems);
        ReadRunKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", seenNames, startupItems);

        var payload = new
        {
            startupAppCount = startupItems.Count,
            highImpactBootContention = startupItems.Count >= 8,
            startupItems
        };

        var summary =
            $"Detected {startupItems.Count} Windows startup application(s) configured to launch at sign-in{(startupItems.Count >= 8 ? " (elevated startup contention)" : "")}.";

        return Task.FromResult(new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        });
    }

    private static void ReadRunKey(
        RegistryKey root,
        string subKeyPath,
        string locationLabel,
        HashSet<string> seenNames,
        List<object> items)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath, writable: false);
            if (key == null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(valueName) || !seenNames.Add(valueName))
                {
                    continue;
                }

                var cmd = key.GetValue(valueName)?.ToString() ?? string.Empty;
                items.Add(new
                {
                    name = valueName,
                    command = cmd,
                    location = locationLabel,
                    user = locationLabel.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ? "All Users" : "Current User"
                });
            }
        }
        catch
        {
            // Ignore registry permission errors
        }
    }
}
