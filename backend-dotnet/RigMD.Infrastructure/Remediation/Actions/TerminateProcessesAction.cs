using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Tools;

namespace RigMD.Infrastructure.Remediation.Actions;

public class TerminateProcessesAction
{
    private static readonly HashSet<string> ProtectedProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "aggregatorhost",
            "applicationframehost",
            "audiodg",
            "backgroundtaskhost",
            "conhost",
            "csrss",
            "ctfmon",
            "dashost",
            "dllhost",
            "dotnet",
            "dwm",
            "explorer",
            "fontdrvhost",
            "idle",
            "lsaiso",
            "lsass",
            "memory compression",
            "memorycompression",
            "mpcmdrun",
            "mpcopyaccelerator",
            "mpdefendercoreservice",
            "mpsigstub",
            "msedgewebview2",
            "msmpeng",
            "mssense",
            "nissrv",
            "registry",
            "rigmd",
            "rigmd.agent",
            "rigmd.api",
            "rigmd.desktop",
            "runtimebroker",
            "searchhost",
            "searchindexer",
            "secure system",
            "securesystem",
            "securityhealthhost",
            "securityhealthservice",
            "securityhealthsystray",
            "sense",
            "service host",
            "servicehost",
            "services",
            "sgrmbroker",
            "shellexperiencehost",
            "sihost",
            "smartscreen",
            "smss",
            "spoolsv",
            "startmenuexperiencehost",
            "svchost",
            "system",
            "system idle process",
            "taskhostw",
            "taskmgr",
            "textinputhost",
            "unsecapp",
            "VGAuthService",
            "vmtoolsd",
            "widgets",
            "widgetservice",
            "wininit",
            "winlogon",
            "wlanext",
            "wmiprvse",
            "wudfhost"
        };

    private readonly ILogger<TerminateProcessesAction> _logger;

    public TerminateProcessesAction(ILogger<TerminateProcessesAction> logger)
    {
        _logger = logger;
    }

    public static string NormalizeProcessName(string processName)
    {
        return (processName ?? string.Empty)
            .Trim()
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedProcess(string processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (ProtectedProcessNames.Contains(normalized))
        {
            return true;
        }

        if (normalized.Contains("defender", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("securityhealth", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("antimalware", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("rigmd", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("webview", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public ToolDryRunPreview Preview(IReadOnlyCollection<string> processNames, string? reason = null)
    {
        var preview = new ToolDryRunPreview
        {
            ToolName = "terminate_processes",
            DisplayName = "Try to close reviewed high-use apps",
            SafetyTier = ToolSafetyTier.Tier2_DestructiveOrAdmin,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = true
        };

        var distinctTargets = (processNames ?? Array.Empty<string>())
            .Select(NormalizeProcessName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctTargets.Count == 0)
        {
            preview.CanExecute = false;
            preview.WhatWillHappen = "No target process names were specified.";
            preview.Warnings.Add("At least one non-protected process name is required.");
            return preview;
        }

        int totalInstances = 0;
        long totalBytes = 0;
        var blockedTargets = new List<string>();

        foreach (var target in distinctTargets)
        {
            if (IsProtectedProcess(target))
            {
                blockedTargets.Add(target);
                preview.Warnings.Add($"Process '{target}' is a protected Windows/RigMD system process and cannot be terminated.");
                continue;
            }

            var matching = Process.GetProcessesByName(target);
            if (matching.Length == 0)
            {
                preview.Warnings.Add($"Process '{target}' is not currently running.");
                continue;
            }

            long groupBytes = 0;
            var pids = new List<int>();
            foreach (var proc in matching)
            {
                try
                {
                    groupBytes += proc.WorkingSet64;
                    pids.Add(proc.Id);
                }
                catch
                {
                    // Ignore inaccessible process handle
                }
                finally
                {
                    proc.Dispose();
                }
            }

            totalInstances += matching.Length;
            totalBytes += groupBytes;
            preview.AffectedTargets.Add(
                $"{target}.exe ({matching.Length} instance(s), ~{ToolArgumentHelper.FormatBytes(groupBytes)}, PIDs: {string.Join(", ", pids.Take(5))})");
        }

        preview.AffectedItemsCount = totalInstances;
        preview.EstimatedBytesAffected = totalBytes;

        if (blockedTargets.Count == distinctTargets.Count)
        {
            preview.CanExecute = false;
            preview.WhatWillHappen = $"Blocked termination of protected system process(es): {string.Join(", ", blockedTargets)}.";
            return preview;
        }

        if (totalInstances == 0)
        {
            preview.CanExecute = false;
            preview.WhatWillHappen = "None of the requested target processes are currently running.";
            return preview;
        }

        var reasonSuffix = !string.IsNullOrWhiteSpace(reason) ? $" Reason: {reason}" : string.Empty;
        preview.CanExecute = true;
        preview.WhatWillHappen =
            $"Try normal close requests for {totalInstances} process instance(s) ({string.Join(", ", preview.AffectedTargets)}). Apps may remain running in the background. No force close is used; the displayed app memory is not guaranteed system memory freed.{reasonSuffix}";
        preview.Warnings.Add("Unsaved work in the target application(s) may be lost when closed.");

        return preview;
    }

    public async Task<ExecutionResult> ExecuteAsync(
        IReadOnlyCollection<string> processNames,
        string? reason = null,
        Action<string>? progressReporter = null)
    {
        var distinctTargets = (processNames ?? Array.Empty<string>())
            .Select(NormalizeProcessName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var logs = new List<string>();
        var proofs = new List<ExecutionProof>();
        int totalClosedGracefully = 0;
        int totalFailed = 0;

        foreach (var target in distinctTargets)
        {
            if (IsProtectedProcess(target))
            {
                var msg = $"BLOCKED: '{target}' is on the protected system process denylist.";
                _logger.LogWarning("TerminateProcessesAction: {Message}", msg);
                logs.Add(msg);
                proofs.Add(new ExecutionProof
                {
                    Label = $"{target}.exe",
                    Status = "Blocked (Protected System Process)",
                    Meaning = "Critical OS and RigMD processes are strictly protected from termination."
                });
                totalFailed++;
                continue;
            }

            var processes = Process.GetProcessesByName(target);
            if (processes.Length == 0)
            {
                logs.Add($"SKIPPED: '{target}.exe' is not currently running.");
                continue;
            }

            int beforeCount = processes.Length;
            long beforeBytes = 0;
            foreach (var p in processes)
            {
                try
                {
                    beforeBytes += p.WorkingSet64;
                }
                catch { }
            }

            progressReporter?.Invoke($"Closing {beforeCount} instance(s) of {target}.exe (~{ToolArgumentHelper.FormatBytes(beforeBytes)})...");

            foreach (var proc in processes)
            {
                try
                {
                    if (!proc.HasExited && proc.CloseMainWindow())
                    {
                        totalClosedGracefully++;
                    }
                }
                catch
                {
                    totalFailed++;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2));

            foreach (var proc in processes)
            {
                proc.Dispose();
            }

            var remaining = Process.GetProcessesByName(target);
            int afterCount = remaining.Length;
            long afterBytes = 0;
            foreach (var r in remaining)
            {
                try
                {
                    afterBytes += r.WorkingSet64;
                }
                catch { }
                finally
                {
                    r.Dispose();
                }
            }

            logs.Add(
                $"OBSERVED '{target}.exe': {beforeCount} -> {afterCount} instance(s), app working set {ToolArgumentHelper.FormatBytes(beforeBytes)} -> {ToolArgumentHelper.FormatBytes(afterBytes)}. No force close.");

            proofs.Add(new ExecutionProof
            {
                Label = $"{target}.exe",
                Status = afterCount == 0 ? "App closed" : "App still running in background",
                Before = $"{beforeCount} proc(s) ({ToolArgumentHelper.FormatBytes(beforeBytes)})",
                After = $"{afterCount} proc(s) ({ToolArgumentHelper.FormatBytes(afterBytes)})",
                Meaning = afterCount == 0
                    ? "No matching app processes were observed. A process working-set change is not a measurement of system-wide memory freed."
                    : "The app has not fully exited. RigMD did not force it to close. Save work before quitting it from the app itself."
            });
        }

        bool anySucceeded = totalClosedGracefully > 0;
        var summary = anySucceeded
            ? $"Sent {totalClosedGracefully} normal close request(s); {totalFailed} blocked attempt(s). No force close was used. Review each app result; a request does not confirm an app exited or memory pressure improved."
            : "No normal close request was accepted. No force close was used. Review each app result.";

        return new ExecutionResult
        {
            Success = anySucceeded,
            Summary = summary,
            OutputLog = string.Join(Environment.NewLine, logs),
            Proof = proofs
        };
    }
}
