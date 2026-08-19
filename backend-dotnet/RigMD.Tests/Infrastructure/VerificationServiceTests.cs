using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation;
using Xunit;

namespace RigMD.Tests.Infrastructure;

public class VerificationServiceTests
{
    [Fact]
    public async Task VerifyAsync_WhenExecutionFails_ReturnsUnresolved()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "clear_user_temp_files" };
        var execution = new ExecutionResult { Success = false };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Unresolved, result);
    }

    [Fact]
    public async Task VerifyAsync_WhenFileCountProofIsMissing_ReturnsUnknown()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "clear_user_temp_files" };
        var execution = new ExecutionResult
        {
            Success = true,
            Proof = new List<ExecutionProof>
            {
                new ExecutionProof { Label = "Directory Size", Status = "Reduced" } // Missing File Count
            }
        };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Unknown, result);
    }

    [Fact]
    public async Task VerifyAsync_WhenFileCountIsReduced_ReturnsResolved()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "clear_user_temp_files" };
        var execution = new ExecutionResult
        {
            Success = true,
            Proof = new List<ExecutionProof>
            {
                new ExecutionProof { Label = "File Count", Status = "Reduced" }
            }
        };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Resolved, result);
    }

    [Fact]
    public async Task VerifyAsync_WhenDirectoryWasAlreadyEmpty_ReturnsResolved()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "clear_user_temp_files" };
        var execution = new ExecutionResult
        {
            Success = true,
            Proof = new List<ExecutionProof>
            {
                new ExecutionProof { Label = "File Count", Before = "0", Status = "Unchanged" }
            }
        };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Resolved, result);
    }

    [Fact]
    public async Task VerifyAsync_WhenFilesAreUnchangedAndNotEmpty_ReturnsUnresolved()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "clear_user_temp_files" };
        var execution = new ExecutionResult
        {
            Success = true, // It might claim success if it ran without errors
            Proof = new List<ExecutionProof>
            {
                new ExecutionProof { Label = "File Count", Before = "10", Status = "Unchanged" }
            }
        };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Unresolved, result);
    }

    [Fact]
    public async Task VerifyAsync_WhenActionIsUnknown_ReturnsUnknown()
    {
        var service = new VerificationService(NullLogger<VerificationService>.Instance);
        var action = new RemediationActionDef { Id = "some_unknown_action" };
        var execution = new ExecutionResult { Success = true };

        var result = await service.VerifyAsync(action, execution, new HardwareProfileDto());

        Assert.Equal(VerificationStatus.Unknown, result);
    }
}
