using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Tools;

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
        if (!ToolArgumentHelper.IsCurrentProcessElevated())
        {
            _logger.LogWarning(
                "SFC scan blocked because the current process is not running with Administrator privileges.");

            return new ExecutionResult
            {
                Success = false,
                Summary =
                    "System File Checker (sfc /scannow) requires Administrator privileges. Please run RigMD as Administrator.",
                OutputLog =
                    "Elevation check failed: Current process is not running in an elevated Administrator role.",
                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "System File Integrity",
                        Before = "Unverified",
                        After = "Blocked (Elevation Required)",
                        Status = "Failed",
                        Meaning = "Administrator privileges are required to execute sfc.exe /scannow."
                    }
                }
            };
        }

        _logger.LogInformation(
            "Starting System File Checker (sfc /scannow)");

        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "sfc.exe",
                Arguments = "/scannow",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.Unicode,
                StandardErrorEncoding = System.Text.Encoding.Unicode,
                CreateNoWindow = true
            };

            var outputLines = new List<string>();
            using var process = new Process { StartInfo = processInfo };

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    var cleaned = e.Data.Replace("\0", string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        outputLines.Add(cleaned);
                        progressReporter?.Invoke($"[SFC] {cleaned}");
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync();

            var exitCode = process.ExitCode;
            var fullOutput = string.Join(Environment.NewLine, outputLines);
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

                OutputLog = string.IsNullOrWhiteSpace(fullOutput)
                    ? $"SFC Exit Code: {exitCode}"
                    : fullOutput,

                Proof = new List<ExecutionProof>
                {
                    new()
                    {
                        Label = "System File Integrity",
                        Before = "Unverified",
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
