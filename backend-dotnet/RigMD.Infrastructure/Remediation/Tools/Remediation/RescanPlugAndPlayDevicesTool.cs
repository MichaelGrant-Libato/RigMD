using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Remediation;

public class RescanPlugAndPlayDevicesTool : IRigMdAgentTool
{
    public string Name => "rescan_plug_and_play_devices";

    public string DisplayName => "Rescan connected devices";

    public string Description =>
        "Asks Windows Plug and Play to rescan connected hardware so missing, recently attached, or failed USB/peripheral devices can be rediscovered without uninstalling drivers or deleting user data.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier2_DestructiveOrAdmin;

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
        var isAdmin = ToolArgumentHelper.IsCurrentProcessElevated();
        var warnings = new List<string>
        {
            "Connected USB or peripheral devices may briefly refresh while Windows checks them again."
        };

        if (!isAdmin)
        {
            warnings.Add("Running the hardware rescan may require Administrator permission.");
        }

        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = isAdmin,
            RequiresAdmin = true,
            IsRunningAsAdmin = isAdmin,
            RequiresUserConfirmation = true,
            AffectedItemsCount = 1,
            EstimatedBytesAffected = 0,
            AffectedTargets = new List<string>
            {
                "Windows Plug and Play device tree (pnputil /scan-devices)"
            },
            Warnings = warnings,
            WhatWillHappen =
                "RigMD will ask Windows to look for connected devices again. This can clear stale USB/peripheral detection state, but it will not uninstall drivers or remove files."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        if (!ToolArgumentHelper.IsCurrentProcessElevated())
        {
            var msg = "Rescanning connected devices requires Administrator permission. Please run RigMD as Administrator.";
            return new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = false,
                Summary = msg,
                DataJson = JsonSerializer.Serialize(new { success = false, error = msg }, ToolArgumentHelper.JsonOptions),
                OutputLog = msg,
                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "Connected device rescan",
                        Before = "Not run",
                        After = "Blocked",
                        Status = "Admin required",
                        Meaning = "Windows needs Administrator permission before RigMD can rescan connected devices."
                    }
                }
            };
        }

        progressReporter?.Invoke("Asking Windows to rescan connected devices...");

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/scan-devices",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            var success = process.ExitCode == 0;
            var log = string.Join(
                Environment.NewLine,
                new[] { output, error, $"Exit Code: {process.ExitCode}" }
                    .Where(line => !string.IsNullOrWhiteSpace(line)));

            var summary = success
                ? "Windows completed the connected-device rescan."
                : $"Windows device rescan finished with exit code {process.ExitCode}.";

            return new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = success,
                Summary = summary,
                DataJson = JsonSerializer.Serialize(new
                {
                    success,
                    exitCode = process.ExitCode,
                    output,
                    error
                }, ToolArgumentHelper.JsonOptions),
                OutputLog = string.IsNullOrWhiteSpace(log) ? summary : log,
                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "Connected device rescan",
                        Before = "Device Manager reported an active device error",
                        After = success ? "Rescan completed" : "Rescan did not complete cleanly",
                        Status = success ? "Completed" : "Failed",
                        Meaning = success
                            ? "Windows checked connected hardware again. Use the follow-up check to confirm whether the Device Manager error cleared."
                            : "Windows could not complete the hardware rescan."
                    }
                }
            };
        }
        catch (Exception ex)
        {
            var msg = $"Windows device rescan failed to start: {ex.Message}";
            return new AgentToolExecutionResult
            {
                ToolName = Name,
                Success = false,
                Summary = "Windows device rescan failed to start.",
                DataJson = JsonSerializer.Serialize(new { success = false, error = msg }, ToolArgumentHelper.JsonOptions),
                OutputLog = msg
            };
        }
    }
}
