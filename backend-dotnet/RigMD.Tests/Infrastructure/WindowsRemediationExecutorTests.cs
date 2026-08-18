using Microsoft.Extensions.Logging;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation;

namespace RigMD.Tests.Infrastructure;

public class WindowsRemediationExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WhenActionIsNotImplemented_FailsSafely()
    {
        using var loggerFactory = LoggerFactory.Create(
            builder =>
            {
            });

        var logger =
            loggerFactory.CreateLogger<WindowsRemediationExecutor>();

        var executor = new WindowsRemediationExecutor(
            logger,
            loggerFactory);

        var action = new RemediationActionDef
        {
            Id = "unknown_test_action",
            Name = "Unknown Test Action",
            Category = "Test",
            RiskLevel = "Low",
            IsReversible = true
        };

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Contains(
            "not yet implemented",
            result.Summary,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "unknown_test_action",
            result.OutputLog);
    }
}