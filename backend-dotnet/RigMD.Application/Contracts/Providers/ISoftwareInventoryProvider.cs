using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Providers;

public interface ISoftwareInventoryProvider
{
    Task<SoftwareInventory<InstalledApp>> ReadAppsAsync(CancellationToken cancellationToken);
    Task<SoftwareInventory<InstalledDriver>> ReadDriversAsync(CancellationToken cancellationToken);
}
