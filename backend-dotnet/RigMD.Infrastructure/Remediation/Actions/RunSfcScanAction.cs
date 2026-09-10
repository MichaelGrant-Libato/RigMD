using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

/// <summary>
/// Runs the Windows System File Checker (sfc /scannow).
///
/// Safety behavior:
/// - SFC is a read-and-repair tool built into Windows.
/// - It scans protected system files and replaces corrupted
///   ones with cached copies from the Windows component store.
/// - Does not delete or modify user data.
/// - Requires administrator privileges (UAC prompt expected).
/// - Records the scan result and output as proof.
/// </summary>
public class RunSfcScanAction
{
    private readonly ILogger<RunSfcScanAction> _logger;

    public RunSfcScanAction(
        ILogger<RunSfcScanAction> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync(Action<string>? progressReporter = null)
    {
        _logger.LogInformation(
            "Starting System File Checker (sfc /scannow)");

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "sfc",
                Arguments = "/scannow",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processInfo };

            if (progressReporter != null)
            {
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        progressReporter($"[SFC] {e.Data}");
                    }
                };
            }

            process.Start();
            
            if (progressReporter != null)
            {
                process.BeginOutputReadLine();
            }

            // SFC can take a while, wait indefinitely or set a high timeout (e.g. 1 hour)
            await process.WaitForExitAsync();

            var exitCode = process.ExitCode;
            var success = exitCode == 0;
            var status = success ? "Scanned" : "Failed";
            var meaning = success 
                ? "Windows System File Checker completed the scan."
                : $"SFC scan exited with code {exitCode}. Administrator privileges may be required.";

            _logger.LogInformation(
                "SFC scan completed: exitCode={ExitCode}, status={Status}",
                exitCode, status);

            return new ExecutionResult
            {
                Success = success,

                Summary = success 
                    ? "System File Checker scan completed successfully."
                    : $"System File Checker scan failed with exit code {exitCode}.",

                OutputLog = $"SFC Exit Code: {exitCode}",

                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "System File Integrity",
                        Before = "Unknown",
                        After = status,
                        Status = status,
                        Meaning = meaning
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SFC scan failed to start");

            return new ExecutionResult
            {
                Success = false,
                Summary =
                    "System File Checker failed to start.",
                OutputLog = ex.Message
            };
        }
    }
}
