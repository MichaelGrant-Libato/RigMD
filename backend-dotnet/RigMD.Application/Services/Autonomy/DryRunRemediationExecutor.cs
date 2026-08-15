using System;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Application.Services.Autonomy;

public class DryRunRemediationExecutor : IRemediationExecutor
{
    public Task<ExecutionResult> ExecuteAsync(RemediationActionDef action)
    {
        var result = new ExecutionResult
        {
            Success = true,
            Summary = $"DRY RUN: Simulated execution of {action.Name}",
            OutputLog = "Simulated output log",
            Proof = new System.Collections.Generic.List<ExecutionProof>
            {
                new ExecutionProof
                {
                    Label = "Dry Run Proof",
                    Status = "Simulated",
                    Meaning = "This is a dry run.",
                    Before = "Simulated before state",
                    After = "Simulated after state"
                }
            }
        };

        return Task.FromResult(result);
    }
}
