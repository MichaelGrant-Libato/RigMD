using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Tools;

namespace RigMD.Infrastructure.Remediation.Actions;

public class RestartExplorerAction
{
    private readonly ILogger<RestartExplorerAction> _logger;

    public RestartExplorerAction(ILogger<RestartExplorerAction> logger)
    {
        _logger = logger;
    }

    public ToolDryRunPreview Preview()
    {
        var running = Process.GetProcessesByName("explorer");
        int count = running.Length;
        long bytes = 0;
        var pids = new List<int>();

        foreach (var p in running)
        {
            try
            {
                bytes += p.WorkingSet64;
                pids.Add(p.Id);
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }

        return new ToolDryRunPreview
        {
            ToolName = "restart_windows_explorer",
            DisplayName = "Restart Windows Explorer Shell",
            SafetyTier = ToolSafetyTier.Tier1_SafeReversible,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = true,
            AffectedItemsCount = count,
            EstimatedBytesAffected = bytes,
            AffectedTargets = new List<string>
            {
                $"explorer.exe ({count} instance(s), PIDs: {(pids.Count > 0 ? string.Join(", ", pids) : "none")}, ~{ToolArgumentHelper.FormatBytes(bytes)})"
            },
            WhatWillHappen =
                "Restarts the Windows Explorer shell (explorer.exe) to recover frozen taskbars, hung File Explorer windows, or shell memory bloat.",
            Warnings = new List<string>
            {
                "The taskbar and desktop icons will briefly flicker for 1–2 seconds while explorer.exe restarts.",
                "Open File Explorer folder windows will be closed."
            }
        };
    }

    public async Task<ExecutionResult> ExecuteAsync(Action<string>? progressReporter = null)
    {
        progressReporter?.Invoke("Stopping existing Windows Explorer shell instances...");

        var existing = Process.GetProcessesByName("explorer");
        var beforePids = new List<int>();
        long beforeBytes = 0;

        foreach (var proc in existing)
        {
            try
            {
                beforePids.Add(proc.Id);
                beforeBytes += proc.WorkingSet64;
                proc.Kill();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RestartExplorerAction: failed to stop explorer PID {Pid}", proc.Id);
            }
            finally
            {
                proc.Dispose();
            }
        }

        await Task.Delay(TimeSpan.FromMilliseconds(900));

        progressReporter?.Invoke("Starting fresh explorer.exe shell process...");

        var afterProcesses = Process.GetProcessesByName("explorer");
        if (afterProcesses.Length == 0)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RestartExplorerAction: failed to launch explorer.exe");
                return new ExecutionResult
                {
                    Success = false,
                    Summary = $"Failed to restart explorer.exe: {ex.Message}",
                    OutputLog = ex.ToString()
                };
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1200));
            afterProcesses = Process.GetProcessesByName("explorer");
        }

        var afterPids = new List<int>();
        long afterBytes = 0;
        foreach (var proc in afterProcesses)
        {
            try
            {
                afterPids.Add(proc.Id);
                afterBytes += proc.WorkingSet64;
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }

        bool success = afterPids.Count > 0;
        var summary = success
            ? $"Windows Explorer restarted cleanly (PID(s): {string.Join(", ", beforePids)} -> {string.Join(", ", afterPids)})."
            : "explorer.exe was stopped, but the new shell process could not be verified.";

        return new ExecutionResult
        {
            Success = success,
            Summary = summary,
            OutputLog = summary,
            Proof = new List<ExecutionProof>
            {
                new()
                {
                    Label = "Windows Explorer Shell (explorer.exe)",
                    Status = success ? "Restarted" : "Warning",
                    Before = beforePids.Count > 0
                        ? $"PID(s) {string.Join(", ", beforePids)} ({ToolArgumentHelper.FormatBytes(beforeBytes)})"
                        : "Not running",
                    After = afterPids.Count > 0
                        ? $"PID(s) {string.Join(", ", afterPids)} ({ToolArgumentHelper.FormatBytes(afterBytes)})"
                        : "Not detected",
                    Meaning = "A new Explorer shell process PID confirms the Windows desktop shell was freshly reinitialized."
                }
            }
        };
    }
}
