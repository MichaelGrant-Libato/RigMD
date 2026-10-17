using RigMD.Application.Models;

namespace RigMD.Application.Services;

internal static class DeviceErrorAttribution
{
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid MonitorClass = new("4d36e96e-e325-11ce-bfc1-08002be10318");

    public static List<DeviceErrorDto> Select(
        IEnumerable<DeviceErrorDto>? errors,
        bool graphicsOnly = false,
        bool includeMonitors = false) =>
        (errors ?? Enumerable.Empty<DeviceErrorDto>())
            .Where(error => error.ErrorCode != 0 && (!graphicsOnly ||
                MatchesClass(error, DisplayClass, "Display") ||
                (includeMonitors && MatchesClass(error, MonitorClass, "Monitor"))))
            .ToList();

    private static bool MatchesClass(DeviceErrorDto error, Guid classGuid, string pnpClass)
    {
        // Device class metadata identifies hardware; friendly names and vendor IDs do not.
        return Guid.TryParse(error.ClassGuid, out var actualClass)
            ? actualClass == classGuid
            : string.Equals(error.PnpClass?.Trim(), pnpClass, StringComparison.OrdinalIgnoreCase);
    }
}
