using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace RigMD.Api.Services;

public sealed record DriveHealthReading(string State, DateTimeOffset CapturedAt, string? Model = null,
    double? Temperature = null, double? WearPercent = null, double? PowerOnHours = null,
    double? UncorrectedReads = null, double? UncorrectedWrites = null);
public sealed record BatteryCapacity(string Name, double? DesignMwh, double? FullChargeMwh, double? CycleCount);
public sealed record BatteryDrain(DateTimeOffset At, double? RemainingMwh, double? DrainedMwh);
public sealed record BatteryHealthReading(string State, DateTimeOffset CapturedAt, BatteryCapacity[] Batteries, BatteryDrain[] Recent);

public static class WindowsHardwareHealth
{
    public static async Task<DriveHealthReading> ReadDriveAsync(int number, CancellationToken token)
    {
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $disk = Get-Disk -Number {{number.ToString(CultureInfo.InvariantCulture)}}
                $measurement = Get-StorageReliabilityCounter -Disk $disk
                [pscustomobject]@{
                    state = 'measured'; model = $disk.FriendlyName
                    temperature = $measurement.Temperature; wearPercent = $measurement.Wear
                    powerOnHours = $measurement.PowerOnHours
                    uncorrectedReads = $measurement.ReadErrorsUncorrected
                    uncorrectedWrites = $measurement.WriteErrorsUncorrected
                } | ConvertTo-Json -Compress
            } catch {
                if ($_.CategoryInfo.Category -in @('PermissionDenied', 'SecurityError')) { exit 5 }
                exit 3
            }
            """;
        var (code, output) = await DeviceDiagnosticRunner.RunPowerShellAsync(script, token);
        var now = DateTimeOffset.UtcNow;
        if (code != 0) return new(code == 5 ? "permission denied" : "unavailable", now);
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var temperature = Number(root, "temperature", 150);
            var wear = Number(root, "wearPercent", 100);
            var hours = Number(root, "powerOnHours");
            var reads = Number(root, "uncorrectedReads");
            var writes = Number(root, "uncorrectedWrites");
            return new(temperature.HasValue || wear.HasValue || hours.HasValue || reads.HasValue || writes.HasValue
                ? "measured" : "unavailable", now,
                root.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null,
                temperature, wear, hours, reads, writes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { return new("unavailable", now); }
    }

    public static async Task<BatteryHealthReading> ReadBatteryAsync(CancellationToken token)
    {
        var directory = Directory.CreateTempSubdirectory("rigmd-battery-");
        var file = Path.Combine(directory.FullName, "battery.xml");
        try
        {
            var (code, _) = await DeviceDiagnosticRunner.RunAsync(Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                ["/batteryreport", "/duration", "3", "/output", file, "/xml"], token);
            if (code != 0 || !File.Exists(file))
                return new("unavailable", DateTimeOffset.UtcNow, [], []);

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 8 * 1024 * 1024 };
            using var reader = XmlReader.Create(file, settings);
            return ParseBatteryReport(XDocument.Load(reader), DateTimeOffset.UtcNow);
        }
        catch (XmlException) { return new("unavailable", DateTimeOffset.UtcNow, [], []); }
        finally
        {
            try { File.Delete(file); directory.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static BatteryHealthReading ParseBatteryReport(XDocument report, DateTimeOffset now)
    {
        var batteries = report.Descendants().Where(node => node.Name.LocalName is "Batteries" or "InstalledBatteries")
            .SelectMany(node => node.Elements().Where(item => item.Name.LocalName == "Battery")).Take(8)
            .Select((item, index) => new BatteryCapacity(
                ReadValue(item, "Name", "Model", "Id") ?? $"Battery {index + 1}",
                Positive(ReadValue(item, "DesignCapacity", "DesignedCapacity")),
                Positive(ReadValue(item, "FullChargeCapacity", "FullChargedCapacity")),
                Positive(ReadValue(item, "CycleCount")))).ToArray();
        var recent = report.Descendants()
            .Where(node => node.Name.LocalName is "RecentUsage" or "BatteryUsage" or "EnergyDrains")
            .SelectMany(node => node.Descendants()).Distinct()
            .Select(node =>
            {
                var timestamp = ReadValue(node, "Timestamp", "StartTime", "Time");
                return DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at) &&
                    at <= now && at >= now.AddDays(-3)
                    ? new BatteryDrain(at, Positive(ReadValue(node, "ChargeCapacity", "RemainingCapacity", "CapacityRemaining")),
                        Positive(ReadValue(node, "Discharge", "EnergyDrained", "EnergyDrain", "Energy")))
                    : null;
            }).OfType<BatteryDrain>().Where(row => row.RemainingMwh.HasValue || row.DrainedMwh.HasValue)
            .OrderByDescending(row => row.At).Take(12).ToArray();
        return new(batteries.Length == 0 && recent.Length == 0 ? "unavailable" : "measured", now, batteries, recent);
    }

    private static string? ReadValue(XElement element, params string[] names) =>
        element.Elements().FirstOrDefault(child => names.Contains(child.Name.LocalName))?.Value
        ?? element.Attributes().FirstOrDefault(attribute => names.Contains(attribute.Name.LocalName))?.Value;

    private static double? Positive(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
        double.IsFinite(number) && number is > 0 and < uint.MaxValue ? number : null;

    private static double? Number(JsonElement root, string name, double ceiling = uint.MaxValue - 1d) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && number >= 0 && number <= ceiling ? number : null;
}
