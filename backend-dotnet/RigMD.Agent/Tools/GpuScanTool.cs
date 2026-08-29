using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class GpuScanTool : IAgentTool
{
    private readonly IGpuProvider _provider;

    public GpuScanTool(IGpuProvider provider)
    {
        _provider = provider;
    }

    public string Id => "scan_gpu";

    public string Description =>
        "Reads GPU model, driver, graphics type, and video memory.";

    public object Execute()
    {
        return _provider.GetGpuStats();
    }
}