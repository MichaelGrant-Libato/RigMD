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
/// </summary>
public class VerificationService : IVerificationService
{
    private readonly ILogger<VerificationService> _logger;

    public VerificationService(ILogger<VerificationService> logger)
    {
        _logger = logger;
    }

    public Task<VerificationStatus> VerifyAsync(RemediationActionDef action, ExecutionResult executionResult, HardwareProfileDto currentSystemState)
    {
        _logger.LogInformation("VerificationService: verifying action '{ActionId}'", action.Id);

        var status = action.Id switch
        {
            "clear_user_temp_files" => VerifyTempFilesCleaned(executionResult),
            "flush_dns" => VerifyFlushDns(executionResult),
            "clear_browser_cache" => VerifyFilesReduced(executionResult, "browser cache"),
            "clear_windows_update_cache" => VerifyFilesReduced(executionResult, "Windows Update cache"),
            "run_disk_cleanup" => VerifyDiskSpaceImproved(executionResult),
            "run_sfc_scan" => VerifySfcScan(executionResult),
            _ => VerificationStatus.Unknown
        };

        _logger.LogInformation("VerificationService: action '{ActionId}' verification result = {Status}",
            action.Id, status);

        return Task.FromResult(status);
    }

    /// <summary>
    /// Checks whether the %TEMP% directory is in a "healthy" state after cleanup.
    /// Uses the exact before/after evidence provided in the ExecutionResult's Proof.
    /// </summary>
    private VerificationStatus VerifyTempFilesCleaned(ExecutionResult executionResult)
    {
        if (executionResult == null || !executionResult.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var countProof = executionResult.Proof.FirstOrDefault(p => p.Label == "File Count");
        var sizeProof = executionResult.Proof.FirstOrDefault(p => p.Label == "Directory Size");

        if (countProof == null)
        {
            return VerificationStatus.Unknown;
        }

        _logger.LogInformation(
            "VerifyTempFilesCleaned: File Count Proof = {Status}, Size Proof = {SizeStatus}",
            countProof.Status, sizeProof?.Status ?? "None");

        if (countProof.Status == "Reduced" || countProof.Before == "0")
        {
            return VerificationStatus.Resolved;
        }

        return VerificationStatus.Unresolved;
    }

    /// <summary>
    /// Verifies that the DNS cache was successfully flushed
    /// by inspecting the execution result proof.
    /// </summary>
    private VerificationStatus VerifyFlushDns(ExecutionResult executionResult)
    {
        if (executionResult == null || !executionResult.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var dnsProof = executionResult.Proof
            .FirstOrDefault(p => p.Label == "DNS Cache Flush");

        if (dnsProof == null)
        {
            return VerificationStatus.Unknown;
        }

        return dnsProof.Status == "Completed"
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    /// <summary>
    /// Generic verifier for file-cleanup actions (browser cache,
    /// Windows Update cache). Checks if any files were reduced.
    /// </summary>
    private VerificationStatus VerifyFilesReduced(
        ExecutionResult executionResult, string context)
    {
        if (executionResult == null || !executionResult.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var filesProof = executionResult.Proof
            .FirstOrDefault(p =>
                p.Label == "Files Deleted" ||
                p.Label == "File Count");

        if (filesProof == null)
        {
            return VerificationStatus.Unknown;
        }

        _logger.LogInformation(
            "VerifyFilesReduced ({Context}): Proof = {Status}",
            context, filesProof.Status);

        return filesProof.Status == "Reduced"
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    /// <summary>
    /// Verifies that Disk Cleanup freed some disk space
    /// by checking the free space proof.
    /// </summary>
    private VerificationStatus VerifyDiskSpaceImproved(
        ExecutionResult executionResult)
    {
        if (executionResult == null || !executionResult.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var spaceProof = executionResult.Proof
            .FirstOrDefault(p => p.Label == "Free Disk Space");

        if (spaceProof == null)
        {
            return VerificationStatus.Unknown;
        }

        return spaceProof.Status == "Improved"
            ? VerificationStatus.Resolved
            : VerificationStatus.Unresolved;
    }

    /// <summary>
    /// Verifies the SFC scan result by inspecting the
    /// integrity status proof.
    /// </summary>
    private VerificationStatus VerifySfcScan(
        ExecutionResult executionResult)
    {
        if (executionResult == null || !executionResult.Success)
        {
            return VerificationStatus.Unresolved;
        }

        var integrityProof = executionResult.Proof
            .FirstOrDefault(p =>
                p.Label == "System File Integrity");

        if (integrityProof == null)
        {
            return VerificationStatus.Unknown;
        }

        return integrityProof.Status switch
        {
            "Healthy" => VerificationStatus.Resolved,
            "Repaired" => VerificationStatus.Resolved,
            "Unrepairable" => VerificationStatus.Unresolved,
            _ => VerificationStatus.Unknown
        };
    }
}

