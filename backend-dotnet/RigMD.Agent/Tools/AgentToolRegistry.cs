namespace RigMD.Agent.Tools;

public class AgentToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public AgentToolRegistry(
        IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(
            tool => tool.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<IAgentTool> GetTools()
    {
        return _tools.Values.ToList().AsReadOnly();
    }

    public bool TryExecute(
        string toolId,
        out object? result)
    {
        if (!_tools.TryGetValue(
                toolId,
                out var tool))
        {
            result = null;
            return false;
        }

        result = tool.Execute();
        return true;
    }
}