using RigMD.Application.Models;

namespace RigMD.Application.Services;

internal static class DeviceErrorAttribution
{
    public static string MetricId(DeviceErrorDto error)
    {
        var device = string.IsNullOrWhiteSpace(error.DeviceId)
            ? error.PnpClass + "|" + error.Name : error.DeviceId;
        var key = device.Trim().ToUpperInvariant() + "|" + error.ErrorCode;
        return "device_error_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..24];
    }
    private static readonly Guid DisplayClass = new("4d36e968-e325-11ce-bfc1-08002be10318");
    private static readonly Guid MonitorClass = new("4d36e96e-e325-11ce-bfc1-08002be10318");

    public static List<DeviceErrorDto> SelectForScope(IEnumerable<DeviceErrorDto>? errors, string mode,
        string scenario, HashSet<string> components)
    {
        var all = Select(errors);
        if (mode == "full" || mode == "component" && components.Contains("drivers") || scenario == "driver-error" || scenario == "blue-screen-crash") return all;
        if (mode == "scenario" && scenario != "no-display") return [];
        return all.Where(e =>
            ((components.Contains("gpu") || components.Contains("display") || scenario == "no-display") &&
                (MatchesClass(e, DisplayClass, "Display") || ((components.Contains("display") || scenario == "no-display") && MatchesClass(e, MonitorClass, "Monitor")))) ||
            (components.Contains("audio") && (MatchesClass(e, new Guid("4d36e96c-e325-11ce-bfc1-08002be10318"), "MEDIA") || string.Equals(e.PnpClass, "AudioEndpoint", StringComparison.OrdinalIgnoreCase))) ||
            (components.Contains("peripherals") && new[] { "USB", "HIDClass", "Keyboard", "Mouse" }.Contains(e.PnpClass, StringComparer.OrdinalIgnoreCase))).ToList();
    }

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
