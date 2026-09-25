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

        var normalized = name.Trim();
        if (_toolsByName.TryGetValue(normalized, out var tool))
        {
            return tool;
        }

        var aliasTarget = normalized.ToLowerInvariant() switch
        {
            "inspect_gpu_status" => "inspect_gpu_and_displays",
            "inspect_dns" => "inspect_network_connectivity",
            "flush_dns" => "flush_dns_cache",
            "clear_user_temp_files" => "clear_temp_files",
            _ => null
        };

        return aliasTarget != null && _toolsByName.TryGetValue(aliasTarget, out var aliasedTool)
            ? aliasedTool
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
