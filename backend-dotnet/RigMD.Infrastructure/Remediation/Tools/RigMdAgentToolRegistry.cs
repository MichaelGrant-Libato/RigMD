using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools;

public class RigMdAgentToolRegistry : IRigMdAgentToolRegistry
{
    private readonly IReadOnlyList<IRigMdAgentTool> _tools;
    private readonly Dictionary<string, IRigMdAgentTool> _toolsByName;

    public RigMdAgentToolRegistry(IEnumerable<IRigMdAgentTool> tools)
    {
        var toolList = tools?.ToList() ?? new List<IRigMdAgentTool>();
        _tools = toolList;
        _toolsByName = new Dictionary<string, IRigMdAgentTool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in toolList)
        {
            _toolsByName[tool.Name] = tool;
        }
    }

    public IReadOnlyList<IRigMdAgentTool> GetAllTools() => _tools;

    public IReadOnlyList<IRigMdAgentTool> GetToolsByTier(ToolSafetyTier tier)
    {
        return _tools
            .Where(t => t.SafetyTier == tier)
            .ToList();
    }

    public IRigMdAgentTool? GetTool(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _toolsByName.TryGetValue(name.Trim(), out var tool)
            ? tool
            : null;
    }

    public IReadOnlyList<AgentToolFunctionDeclaration> GetFunctionDeclarations(bool includeWriteTools = true)
    {
        return _tools
            .Where(t => includeWriteTools || t.SafetyTier == ToolSafetyTier.Tier0_ReadOnly)
            .Select(t => t.GetFunctionDeclaration())
            .ToList();
    }
}
