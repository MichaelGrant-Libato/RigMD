using System;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Contracts.Autonomy;

public interface IAutonomousOrchestrator
{
    Task<OrchestrationResult> RunDryRunCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        Action<string>? progressReporter = null,
        Action<ReActTraceStep>? stepReporter = null,
        CancellationToken cancellationToken = default);

    Task<OrchestrationResult> RunExecutionCycleAsync(
        DiagnosticOutput diagnostic,
        HardwareProfileDto hardware,
        bool userConsentProvided = false,
        Action<string>? progressReporter = null,
        string? requestedToolName = null,
        string? requestedArgumentsJson = null,
        Action<ReActTraceStep>? stepReporter = null,
        CancellationToken cancellationToken = default);
}