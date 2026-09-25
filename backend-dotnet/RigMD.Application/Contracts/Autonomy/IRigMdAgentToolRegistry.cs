using System.Collections.Generic;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

/// <summary>
/// Registry of all diagnostic (Tier 0) and remediation (Tier 1 / Tier 2) tools available to the RigMD agent.
/// </summary>
public interface IRigMdAgentToolRegistry
{
    IReadOnlyList<IRigMdAgentTool> GetAllTools();
    IReadOnlyList<IRigMdAgentTool> GetToolsByTier(ToolSafetyTier tier);
    IRigMdAgentTool? GetTool(string name);
    IReadOnlyList<AgentToolFunctionDeclaration> GetFunctionDeclarations(bool includeWriteTools = true);
}
