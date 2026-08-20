using System;
using System.Collections.Generic;
using RigMD.Domain.Entities;

namespace RigMD.Application.Models;

public class RemediationActionDef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty; // e.g. Low, Medium, High
    public bool IsReversible { get; set; }
    public bool RequiresUserConfirmation { get; set; }
}

public class RemediationPlan
{
    public string SessionId { get; set; } = string.Empty;
    public List<RemediationActionDef> PlannedActions { get; set; } = new();
    public string StrategyReasoning { get; set; } = string.Empty;
}

public class SafetyEvaluation
{
    public bool IsApproved { get; set; }
    public bool RequiresUserConfirmation { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string OutputLog { get; set; } = string.Empty;
    public List<ExecutionProof> Proof { get; set; } = new();
}

public class ExecutionProof
{
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Meaning { get; set; } = string.Empty;
    public string? Before { get; set; }
    public string? After { get; set; }
}

public enum VerificationStatus
{
    Resolved,
    Unresolved,
    Worse,
    Unknown
}

public class OrchestrationResult
{
    public RemediationPlan? Plan { get; set; }

    public SafetyEvaluation? Safety { get; set; }

    public ExecutionResult? Execution { get; set; }

    public VerificationStatus? Verification { get; set; }

    public List<RemediationAttempt> Attempts { get; set; } = new();

    public bool Escalated { get; set; }

    public string Trace { get; set; } = string.Empty;
}

public enum RemediationAttemptState
{
    Planned,
    SafetyRejected,
    AwaitingConsent,
    Executing,
    ExecutionFailed,
    VerificationPending,
    Resolved,
    Unresolved,
    Worse,
    VerificationUnknown,
    RollbackPending,
    RolledBack,
    RollbackFailed,
    PivotPending,
    Pivoted,
    Completed
}

public class RemediationAttempt
{
    public RemediationActionDef? Action { get; set; }

    public RemediationAttemptState State { get; set; }

    public ExecutionResult? Execution { get; set; }

    public VerificationStatus? Verification { get; set; }

    public ExecutionResult? RollbackResult { get; set; }

    public string Notes { get; set; } = string.Empty;
}