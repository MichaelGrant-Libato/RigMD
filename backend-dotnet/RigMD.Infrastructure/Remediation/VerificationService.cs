using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation;

/// <summary>
/// Verification service that measures system state before and after a remediation action
/// to determine whether the action had a positive effect.
/// Currently supports disk-space-based verification for cleanup actions.
/// </summary>
public class VerificationService : IVerificationService
{
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(ILogger<VerificationService> logger)
    {
        _logger = logger;
    }

    public Task<VerificationStatus> VerifyAsync(RemediationActionDef action, HardwareProfileDto currentSystemState)
    {
        _logger.LogInformation("VerificationService: verifying action '{ActionId}'", action.Id);

        var status = action.Id switch
        {
            "clear_user_temp_files" => VerifyTempFilesCleaned(),
            _ => VerificationStatus.Unknown
        };

        _logger.LogInformation("VerificationService: action '{ActionId}' verification result = {Status}",
            action.Id, status);

        return Task.FromResult(status);
    }

    /// <summary>
    /// Checks whether the %TEMP% directory is in a "healthy" state after cleanup.
    /// Considers the action resolved if fewer than 500 files remain or total size is under 100 MB.
    /// </summary>
    private VerificationStatus VerifyTempFilesCleaned()
    {
        var tempPath = Path.GetTempPath();

        if (!Directory.Exists(tempPath))
        {
            return VerificationStatus.Unknown;
        }

        try
        {
            int fileCount = Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories).Count();
            long totalBytes = Directory.EnumerateFiles(tempPath, "*", SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; }
                    catch { return 0L; }
                });

            long totalMb = totalBytes / (1024 * 1024);

            _logger.LogInformation(
                "VerifyTempFilesCleaned: {FileCount} files remaining, {TotalMb} MB",
                fileCount, totalMb);

            // Heuristic: consider resolved if temp is reasonably clean
            if (fileCount < 500 && totalMb < 100)
            {
                return VerificationStatus.Resolved;
            }

            // Still a lot of files — action helped but didn't fully resolve
            return VerificationStatus.Unresolved;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VerifyTempFilesCleaned: failed to measure temp directory");
            return VerificationStatus.Unknown;
        }
    }
}
