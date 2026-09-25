using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectMemoryAndProcessesTool : IRigMdAgentTool
{
    private readonly IMemoryProvider _memoryProvider;
    private readonly IProcessProvider _processProvider;

    public InspectMemoryAndProcessesTool(
        IMemoryProvider memoryProvider,
        IProcessProvider processProvider)
    {
        _memoryProvider = memoryProvider;
        _processProvider = processProvider;
    }

    public string Name => "inspect_memory_and_processes";

    public string DisplayName => "Inspect RAM & Active Processes";

    public string Description =>
        "Reads live physical and committed RAM usage and enumerates the top memory-consuming processes grouped by process name, including instance counts, working set MB, and whether each process is safe to terminate.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["topN"] = new()
                {
                    Type = "integer",
                    Description = "Number of top process groups to return (default 10, max 25)."
                },
                ["sortBy"] = new()
                {
                    Type = "string",
                    Description = "Sort criteria for process groups: 'memory' or 'instances'.",
                    EnumValues = new List<string> { "memory", "instances" }
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
            WhatWillHappen = "Reads physical memory telemetry and lists top running processes without modifying system state."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Inspecting RAM utilization and enumerating top processes...");

        var topN = Math.Clamp(ToolArgumentHelper.GetInt(arguments, "topN", 10), 1, 25);
        var sortBy = ToolArgumentHelper.GetString(arguments, "sortBy", "memory");

        var memory = _memoryProvider.GetMemoryStats();
        var insights = _processProvider.GetProcessInsights();

        var liveGroups = new List<object>();
        try
        {
            var processes = Process.GetProcesses();
            var grouped = processes
                .Select(p =>
                {
                    try
                    {
                        return new
                        {
                            Name = p.ProcessName,
                            Pid = p.Id,
                            WorkingSetBytes = p.WorkingSet64
                        };
                    }
                    catch
                    {
                        return null;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                })
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var totalMb = Math.Round(g.Sum(x => x!.WorkingSetBytes) / (1024.0 * 1024.0), 1);
                    var count = g.Count();
                    var isProtected = TerminateProcessesAction.IsProtectedProcess(g.Key);
                    return new
                    {
                        processName = g.Key,
                        instanceCount = count,
                        memoryMb = totalMb,
                        samplePids = g.Take(5).Select(x => x!.Pid).ToArray(),
                        canTerminateSafely = !isProtected
                    };
                });

            grouped = string.Equals(sortBy, "instances", StringComparison.OrdinalIgnoreCase)
                ? grouped.OrderByDescending(g => g.instanceCount).ThenByDescending(g => g.memoryMb)
                : grouped.OrderByDescending(g => g.memoryMb);

            liveGroups.AddRange(grouped.Take(topN));
        }
        catch
        {
            // Fallback to ProcessInsights if direct enumeration fails
        }

        var payload = new
        {
            memory = new
            {
                totalGb = Math.Round(memory.TotalGb, 2),
                usedGb = Math.Round(memory.UsedGb, 2),
                usagePercent = Math.Round(memory.UsagePercent, 1),
                committedGb = Math.Round(memory.CommittedGb, 2),
                cachedGb = Math.Round(memory.CachedGb, 2),
                speedMtps = memory.SpeedMtps,
                formFactor = memory.FormFactor
            },
            browserSummary = new
            {
                browserDetected = insights.BrowserDetected,
                browserProcessCount = insights.BrowserProcessCount,
                browserMemoryMb = Math.Round(insights.BrowserMemoryMb, 1),
                browserHeavy = insights.BrowserHeavy
            },
            gameSummary = new
            {
                gameDetected = insights.GameDetected,
                gameProcesses = insights.GameProcesses
            },
            memoryLeakWarning = insights.MemoryLeakWarning,
            topProcesses = liveGroups
        };

        var summary =
            $"RAM usage: {memory.UsedGb:F1}/{memory.TotalGb:F1} GB ({memory.UsagePercent:F1}%). Browser RAM: {insights.BrowserMemoryMb:F0} MB across {insights.BrowserProcessCount} processes.";

        return Task.FromResult(new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        });
    }
}
