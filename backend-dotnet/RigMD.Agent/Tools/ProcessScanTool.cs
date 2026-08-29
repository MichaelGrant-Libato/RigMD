using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class ProcessScanTool : IAgentTool
{
    private readonly IProcessProvider _provider;

    public ProcessScanTool(IProcessProvider provider)
    {
        _provider = provider;
    }

    public string Id => "scan_processes";

    public string Description =>
        "Reads process workload and high-memory application information.";

    public object Execute()
    {
        return _provider.GetProcessInsights();
    }
}