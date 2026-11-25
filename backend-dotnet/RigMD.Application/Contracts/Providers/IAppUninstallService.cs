using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Providers;

public interface IAppUninstallService
{
    Task<AppUninstallReview> ReviewAsync(string appId, CancellationToken cancellationToken);
    Task<AppUninstallResult> UninstallAsync(Guid approvalId, CancellationToken cancellationToken);
}
