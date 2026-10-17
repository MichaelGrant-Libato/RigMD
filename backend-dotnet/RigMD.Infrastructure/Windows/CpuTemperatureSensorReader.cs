using System.Globalization;
using System.Management;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public sealed record HardwareTemperatureSample(
    string HardwareType,
    string HardwareIdentifier,
    string SensorIdentifier,
    string SensorType,
    double? Celsius);

public static class CpuTemperatureSensorReader
{
    public static CpuTemperatureReading? Read(
        Func<string, IEnumerable<HardwareTemperatureSample>>? probe = null)
    {
        probe ??= ReadWmiSensors;
        foreach (var source in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
        {
            try
            {
                var reading = SelectReading(probe(source), source);
                if (reading != null) return reading;
            }
            catch
            {
                // A missing or inaccessible provider is unavailable, not an estimated temperature.
            }
        }

        return null;
    }

    public static CpuTemperatureReading? SelectReading(
        IEnumerable<HardwareTemperatureSample> samples,
        string source)
    {
        var hottest = samples
            .Where(sample => string.Equals(sample.HardwareType, "Cpu", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sample.SensorType, "Temperature", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(sample.HardwareIdentifier) &&
                sample.SensorIdentifier.StartsWith(sample.HardwareIdentifier + "/temperature/", StringComparison.OrdinalIgnoreCase) &&
                sample.Celsius is >= 0 and <= 150 && double.IsFinite(sample.Celsius.Value))
            .OrderByDescending(sample => sample.Celsius)
            .FirstOrDefault();

        // CapturedAt is the WMI read time, not a hardware-provided sample timestamp.
        return hottest == null ? null : new CpuTemperatureReading(
            Math.Round(hottest.Celsius!.Value, 1), source, hottest.SensorIdentifier, DateTimeOffset.UtcNow);
    }

    private static IEnumerable<HardwareTemperatureSample> ReadWmiSensors(string source)
    {
        var hardwareTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var hardware = new ManagementObjectSearcher(source, "SELECT Identifier, HardwareType FROM Hardware"))
        {
            foreach (ManagementObject item in hardware.Get())
            {
                var identifier = item["Identifier"]?.ToString();
                if (!string.IsNullOrWhiteSpace(identifier))
                    hardwareTypes[identifier] = item["HardwareType"]?.ToString() ?? string.Empty;
            }
        }

        var samples = new List<HardwareTemperatureSample>();
        using var sensors = new ManagementObjectSearcher(source,
            "SELECT Identifier, Parent, SensorType, Value FROM Sensor WHERE SensorType='Temperature'");
        foreach (ManagementObject item in sensors.Get())
        {
            var parent = item["Parent"]?.ToString() ?? string.Empty;
            if (!hardwareTypes.TryGetValue(parent, out var hardwareType)) continue;
            var rawValue = item["Value"];
            var value = rawValue == null ? (double?)null : Convert.ToDouble(rawValue, CultureInfo.InvariantCulture);
            samples.Add(new HardwareTemperatureSample(hardwareType, parent,
                item["Identifier"]?.ToString() ?? string.Empty, item["SensorType"]?.ToString() ?? string.Empty, value));
        }

        return samples;
    }
}
