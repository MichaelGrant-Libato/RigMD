using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Actions;

public class FlushDnsAction
{
    private readonly ILogger<FlushDnsAction> _logger;

    public FlushDnsAction(
        ILogger<FlushDnsAction> logger)
    {
        _logger = logger;
    }

    public async Task<ExecutionResult> ExecuteAsync()
    {
        var startInfo =
            new ProcessStartInfo
            {
                FileName =
                    "ipconfig.exe",

                Arguments =
                    "/flushdns",

                UseShellExecute =
                    false,

                RedirectStandardOutput =
                    true,

                RedirectStandardError =
                    true,

                CreateNoWindow =
                    true
            };

        try
        {
            _logger.LogInformation(
                "Executing allowlisted DNS cache flush.");

            using var process =
                new Process
                {
                    StartInfo =
                        startInfo
                };

            if (!process.Start())
            {
                return new ExecutionResult
                {
                    Success =
                        false,

                    Summary =
                        "Windows DNS cache flush could not be started.",

                    OutputLog =
                        "ipconfig.exe did not start."
                };
            }

            var standardOutputTask =
                process.StandardOutput
                    .ReadToEndAsync();

            var standardErrorTask =
                process.StandardError
                    .ReadToEndAsync();

            await process
                .WaitForExitAsync();

            var standardOutput =
                await standardOutputTask;

            var standardError =
                await standardErrorTask;

            var success =
                process.ExitCode == 0;

            var outputLog =
                string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        $"Executable: {startInfo.FileName}",
                        $"Arguments: {startInfo.Arguments}",
                        $"Exit code: {process.ExitCode}",
                        $"Standard output: {standardOutput.Trim()}",
                        $"Standard error: {standardError.Trim()}"
                    });

            return new ExecutionResult
            {
                Success =
                    success,

                Summary =
                    success
                        ? "Windows DNS resolver cache was flushed successfully."
                        : "Windows DNS resolver cache flush failed.",

                OutputLog =
                    outputLog,

                Proof =
                    new List<ExecutionProof>
                    {
                        new()
                        {
                            Label =
                                "DNS Cache Flush",

                            Before =
                                "Cached",

                            After =
                                success
                                    ? "Flushed"
                                    : "Unknown",

                            Status =
                                success
                                    ? "Completed"
                                    : "Failed",

                            Meaning =
                                success
                                    ? "Windows reported that the DNS resolver cache was flushed."
                                    : "Windows did not confirm that the DNS resolver cache was flushed."
                        }
                    }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "DNS cache flush failed.");

            return new ExecutionResult
            {
                Success =
                    false,

                Summary =
                    "Windows DNS resolver cache flush failed.",

                OutputLog =
                    ex.Message
            };
        }
    }
}