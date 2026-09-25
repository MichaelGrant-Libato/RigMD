using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

/// <summary>
/// Represents a single callable diagnostic or remediation tool exposed to the LLM reasoning engine.
/// </summary>
public interface IRigMdAgentTool
{
    /// <summary>
    /// Unique snake_case tool identifier used in LLM function declarations (e.g., "inspect_cpu_and_thermals").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable display name shown in the UI timeline and approval cards.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Detailed description of what the tool inspects or remediates and when the LLM should invoke it.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Safety tier governing whether the tool can be auto-executed (Tier 0) or requires a dry-run preview and user approval (Tier 1 / Tier 2).
    /// </summary>
    ToolSafetyTier SafetyTier { get; }

    /// <summary>
    /// Returns the JSON Schema function declaration for LLM tool-calling.
    /// </summary>
    AgentToolFunctionDeclaration GetFunctionDeclaration();

    /// <summary>
    /// Performs a real, non-destructive pre-flight check (measuring bytes, counting files, verifying PIDs, checking admin elevation)
    /// without mutating system state.
    /// </summary>
    Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the tool against the live Windows OS / WMI / LibreHardwareMonitor providers and returns structured JSON observations.
    /// </summary>
    Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default);
}
