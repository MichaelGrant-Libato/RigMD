using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class MemoryScanTool : IAgentTool
{
    private readonly IMemoryProvider _provider;

    public MemoryScanTool(IMemoryProvider provider)
    {
        _provider = provider;
    }

    public string Id => "scan_memory";

    public string Description =>
        "Reads total memory, used memory, and memory utilization.";

    public object Execute()
    {
        return _provider.GetMemoryStats();
    }
}