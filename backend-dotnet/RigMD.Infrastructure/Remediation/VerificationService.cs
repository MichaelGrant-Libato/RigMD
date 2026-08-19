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

    public Task<VerificationStatus> VerifyAsync(RemediationActionDef action, ExecutionResult executionResult, HardwareProfileDto currentSystemState)
    {
        _logger.LogInformation("VerificationService: verifying action '{ActionId}'", action.Id);

        var status = action.Id switch
        {
            "clear_user_temp_files" => VerifyTempFilesCleaned(executionResult),
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
}
