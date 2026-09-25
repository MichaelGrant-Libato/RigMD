using System.Collections.Generic;

namespace RigMD.Application.Models;

/// <summary>
/// Safety classification for agent tools in the ReAct loop.
/// </summary>
public enum ToolSafetyTier
{
    /// <summary>
    /// Pure read-only hardware/OS telemetry inspection. Auto-executed by the agent during reasoning.
    /// </summary>
    Tier0_ReadOnly = 0,

    /// <summary>
    /// Low-risk, reversible or non-destructive OS maintenance (e.g., DNS flush, temp cleanup, Explorer restart).
    /// </summary>
    Tier1_SafeReversible = 1,

    /// <summary>
    /// Higher-impact or administrator-privileged OS remediation (e.g., process termination, browser cache clear, Windows Update cache reset, SFC scan).
    /// Always requires dry-run impact preview and explicit user confirmation.
    /// </summary>
    Tier2_DestructiveOrAdmin = 2
}

/// <summary>
/// Describes a single parameter property for an LLM function declaration (OpenAPI / Gemini JSON Schema compatible).
/// </summary>
public class AgentToolParameterProperty
{
    public string Type { get; set; } = "string";
    public string Description { get; set; } = string.Empty;
    public List<string>? EnumValues { get; set; }
    public string? ItemsType { get; set; }
}

/// <summary>
/// Provider-agnostic function declaration schema for LLM tool-calling.
/// </summary>
public class AgentToolFunctionDeclaration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, AgentToolParameterProperty> Parameters { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Real pre-execution dry-run impact analysis for Tier 1 and Tier 2 remediation tools.
/// </summary>
public class ToolDryRunPreview
{
    public string ToolName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ToolSafetyTier SafetyTier { get; set; }
    public bool CanExecute { get; set; } = true;
    public bool RequiresAdmin { get; set; }
    public bool IsRunningAsAdmin { get; set; }
    public bool RequiresUserConfirmation { get; set; }
    public string WhatWillHappen { get; set; } = string.Empty;
    public int AffectedItemsCount { get; set; }
    public long EstimatedBytesAffected { get; set; }
    public List<string> AffectedTargets { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Result of executing an <see cref="RigMD.Application.Contracts.Autonomy.IRigMdAgentTool"/>.
/// </summary>
public class AgentToolExecutionResult
{
    public string ToolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public string OutputLog { get; set; } = string.Empty;
    public List<ExecutionProof> Proof { get; set; } = new();
}
