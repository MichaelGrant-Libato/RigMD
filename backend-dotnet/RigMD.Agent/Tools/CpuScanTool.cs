using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class CpuScanTool : IAgentTool
{
    private readonly ICpuProvider _provider;

    public CpuScanTool(ICpuProvider provider)
    {
        _provider = provider;
    }

    public string Id => "scan_cpu";

    public string Description =>
        "Reads CPU model, utilization, cores, threads, and frequency.";

    public object Execute()
    {
        return _provider.GetCpuStats();
    }
}