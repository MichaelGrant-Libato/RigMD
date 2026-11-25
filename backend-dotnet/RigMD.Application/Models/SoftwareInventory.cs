namespace RigMD.Application.Models;

public sealed record InstalledApp(
    string Id, string Name, string? Publisher, string? Version, string? InstallPath, long? EstimatedSizeBytes,
    string Source, string? Volume = null, int? DiskIndex = null);

public sealed record InstalledDriver(
    string Id, string Name, string? Provider, string? Version, string Source, string? Inf, string? Status,
    string? Details);

public sealed record SoftwareInventory<T>(IReadOnlyList<T> Entries, bool Partial);
