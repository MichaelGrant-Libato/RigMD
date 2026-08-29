using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent.Tools;

public class StorageScanTool : IAgentTool
{
    private readonly IStorageProvider _provider;

    public StorageScanTool(IStorageProvider provider)
    {
        _provider = provider;
    }

    public string Id => "scan_storage";

    public string Description =>
        "Reads physical storage devices and logical disk usage.";

    public object Execute()
    {
        return new
        {
            PrimaryStorageType =
                _provider.GetPrimaryStorageType(),

            StorageDrives =
                _provider.GetStorageDrives(),

            Volumes =
                _provider.GetAllDisks()
        };
    }
}