using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace RigMD.Api.Services;

public sealed record PackageRemovalTarget(string FullName, string FamilyName, string UserSid);
public sealed record PackageFacts(string FullName, string FamilyName, string UserSid, string Name, string Publisher, string Version,
    bool Framework, bool Resource, bool Bundle, bool Optional, bool Development, bool SystemSigned, bool SignatureVerified, bool Healthy, bool HasAppEntry, bool RequiredByAnotherPackage);

public static class WindowsPackageInventory
{
    public static StorageAppEntry FromFacts(PackageFacts facts)
    {
        var protectedApp = facts.Framework || facts.Resource || facts.Bundle || facts.Optional || facts.Development || facts.SystemSigned ||
            !facts.SignatureVerified || !facts.Healthy || !facts.HasAppEntry || facts.RequiredByAnotherPackage || StorageAppInventory.ProtectedApp(facts.Name, facts.Publisher);
        var allowed = !protectedApp && !string.IsNullOrWhiteSpace(facts.UserSid) && !string.IsNullOrWhiteSpace(facts.FullName) && !string.IsNullOrWhiteSpace(facts.FamilyName);
        return new(facts.Name, facts.Publisher, facts.Version, null, "Current Windows account package")
        {
            Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"appx:{facts.UserSid}:{facts.FullName}"))),
            SourceType = "store", CanUninstall = allowed, UninstallMethod = allowed ? "windows_package" : "unsupported",
            PackageTarget = allowed ? new(facts.FullName, facts.FamilyName, facts.UserSid) : null,
            UninstallReason = allowed ? "Windows package removal for this account. Windows will enforce its removal policies; review and approval are required."
                : "This package is system-managed, shared, protected, or could not be verified for this account. RigMD will not force its removal."
        };
    }

    public static StoreInventorySnapshot Capture(CancellationToken cancellationToken)
    {
        var apps = new List<StorageAppEntry>(); var partial = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Current account unavailable.");
            // An over-the-shoulder elevation must not offer to remove a different account's packages.
            if (NativeUninstallRunner.DesktopUser() != sid) partial = true;
            var packages = new PackageManager().FindPackagesForUser(sid).Take(1025).ToArray();
            if (packages.Length > 1024) partial = true;
            foreach (var package in packages.Take(1024))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Match the user-facing main-app view; dependencies/resources are never direct-removal choices.
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle || package.IsOptional) continue;
                    var required = packages.Any(other => other.Id.FullName != package.Id.FullName && other.Dependencies.Any(dependency => dependency.Id.FamilyName == package.Id.FamilyName));
                    var version = package.Id.Version;
                    var facts = new PackageFacts(package.Id.FullName, package.Id.FamilyName, sid,
                        Clean(package.DisplayName.Length > 0 ? package.DisplayName : package.Id.Name), Clean(package.PublisherDisplayName),
                        $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}", package.IsFramework, package.IsResourcePackage, package.IsBundle,
                        package.IsOptional, package.IsDevelopmentMode, package.SignatureKind == PackageSignatureKind.System,
                        package.SignatureKind is PackageSignatureKind.Store or PackageSignatureKind.Enterprise, package.Status.VerifyIsOK(),
                        package.GetAppListEntries().Count > 0, required || partial);
                    apps.Add(FromFacts(facts));
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or IOException) { partial = true; }
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException or System.Security.SecurityException or TypeLoadException) { partial = true; }
        return new(partial ? apps.Select(app => app with { CanUninstall = false, PackageTarget = null,
            UninstallMethod = "none", UninstallReason = "Windows package inventory is incomplete. Update the list or review this app in Windows." }).ToArray() : apps, partial);
    }

    private static string Clean(string value) => new(value.Where(character => !char.IsControl(character)).Take(200).ToArray());

    public static async Task<int> RemoveAsync(StorageAppEntry expected)
    {
        var target = expected.PackageTarget ?? throw new InvalidOperationException("No verified package identity.");
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != target.UserSid) throw new InvalidOperationException("The Windows account changed.");
        var current = Capture(CancellationToken.None);
        var matching = current.Apps.Where(app => app.Id == expected.Id).ToArray();
        if (current.Partial || matching.Length != 1 || matching[0] != expected || !matching[0].CanUninstall)
            throw new InvalidOperationException("Package state changed. Review a fresh preview.");
        // Current account only; Windows manages any now-unused package dependencies.
        // No all-users or deprovisioning request is made.
        var result = await new PackageManager().RemovePackageAsync(target.FullName, RemovalOptions.None);
        return result.ExtendedErrorCode?.HResult ?? 0;
    }
}
