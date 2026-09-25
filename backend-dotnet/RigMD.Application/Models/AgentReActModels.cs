using System;
using System.Collections.Generic;

namespace RigMD.Application.Models;

public enum ReActStepType
{
    Thought,
    ToolCall,
    Observation,
    DryRunPreview,
    AwaitingApproval,
    Execution,
    Verification,
    Conclusion
}

/// <summary>
/// A single transparent step in the agent's Thought -> Action -> Observation trace.
/// </summary>
public class ReActTraceStep
{
    public int StepIndex { get; set; }
    public string StepType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ToolName { get; set; }
    public string? ToolArgumentsJson { get; set; }
    public string? ObservationJson { get; set; }
    public long DurationMs { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a tool call requested by the LLM during a ReAct turn.
/// </summary>
public class ReActToolCallRequest
{
    public string ToolName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string Thought { get; set; } = string.Empty;
}

/// <summary>
/// Represents a completed tool observation in the ReAct conversation history.
/// </summary>
public class ReActTurnRecord
{
    public int TurnNumber { get; set; }
    public string Thought { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string ObservationSummary { get; set; } = string.Empty;
    public string ObservationJson { get; set; } = "{}";
}

/// <summary>
/// Input context passed to <see cref="RigMD.Application.Contracts.Autonomy.IReActLlmClient"/> on each turn.
/// </summary>
public class ReActConversationContext
{
    public string SessionId { get; set; } = string.Empty;
    public string UserSymptom { get; set; } = string.Empty;
    public string DiagnosedCategory { get; set; } = string.Empty;
    public string InitialSummary { get; set; } = string.Empty;
    public int CurrentTurn { get; set; }
    public int MaxTurns { get; set; } = 4;
    public List<ReActTurnRecord> History { get; set; } = new();
}

/// <summary>
/// Final root-cause diagnosis and remediation proposal produced by the ReAct reasoning loop.
/// </summary>
public class ReActFinalProposal
{
    public string RootCauseAnalysis { get; set; } = string.Empty;
    public string ConfidenceLevel { get; set; } = "High";
    public string? RecommendedToolName { get; set; }
    public string RecommendedToolArgumentsJson { get; set; } = "{}";
    public string RemediationRationale { get; set; } = string.Empty;
    public List<string> EvidenceCitations { get; set; } = new();
}

/// <summary>
/// Decision returned by the LLM (or local tool-calling fallback) for a single ReAct turn.
/// </summary>
public class ReActModelTurnDecision
{
    public string EngineName { get; set; } = "Gemini-2.5-Flash";
    public string Thought { get; set; } = string.Empty;
    public List<ReActToolCallRequest> ToolCalls { get; set; } = new();
    public ReActFinalProposal? FinalProposal { get; set; }
}

/// <summary>
/// Details of the specific remediation tool proposed by the agent along with its live dry-run preview.
/// </summary>
public class ProposedToolInvocation
{
    public string ToolName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SafetyTier { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string RootCauseAnalysis { get; set; } = string.Empty;
    public string RemediationRationale { get; set; } = string.Empty;
    public List<string> EvidenceCitations { get; set; } = new();
    public ToolDryRunPreview? DryRunPreview { get; set; }
}

/// <summary>
/// Real post-execution telemetry verification report comparing before vs. after hardware/OS metrics.
/// </summary>
public class PostExecutionVerificationReport
{
    public string VerificationToolName { get; set; } = string.Empty;
    public VerificationStatus Status { get; set; } = VerificationStatus.Unknown;
    public string Summary { get; set; } = string.Empty;
    public string BeforeSnapshotJson { get; set; } = "{}";
    public string AfterSnapshotJson { get; set; } = "{}";
    public List<ExecutionProof> MetricDeltas { get; set; } = new();
}
