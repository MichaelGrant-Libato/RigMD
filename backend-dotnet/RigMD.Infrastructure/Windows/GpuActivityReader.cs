using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

internal sealed partial class GpuActivityReader
{
    private IReadOnlyList<GpuAdapterIdentity> _inventory = [];
    private long _lastInventoryRead;

    public IReadOnlyList<GpuAdapterReading> Read()
    {
        if (_lastInventoryRead == 0 || Stopwatch.GetElapsedTime(_lastInventoryRead) >= TimeSpan.FromSeconds(30))
        {
            _inventory = DxCoreGpuInventory.Read();
            _lastInventoryRead = Stopwatch.GetTimestamp();
        }

        var samples = new List<(string Name, double Usage)>();
        try
        {
            using var query = new ManagementObjectSearcher(@"root\CIMV2",
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            query.Options.Timeout = TimeSpan.FromSeconds(2);
            query.Options.Rewindable = false;
            using var rows = query.Get();
            var started = Stopwatch.GetTimestamp();
            foreach (ManagementObject row in rows)
            {
                using (row)
                {
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2) || samples.Count >= 32_768)
                    {
                        samples.Clear();
                        break;
                    }
                    if (row["Name"] is string name && double.TryParse(row["UtilizationPercentage"]?.ToString(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        samples.Add((name, value));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            // A truncated enumeration cannot establish utilization, even if it returned some engines.
            samples.Clear();
        }

        return ReadTemperatures(BuildReadings(_inventory, samples, DateTimeOffset.UtcNow));
    }

    internal static IReadOnlyList<GpuAdapterReading> ReadTemperatures(IReadOnlyList<GpuAdapterReading> adapters,
        Func<ulong, double?>? readTemperature = null)
    {
        readTemperature ??= WindowsGpuTemperatureReader.Read;
        return adapters.Select(adapter =>
        {
            var temperature = readTemperature(ulong.Parse(adapter.Id, NumberStyles.HexNumber));
            return adapter with
            {
                TemperatureCelsius = temperature,
                TemperatureSource = temperature.HasValue ? WindowsGpuTemperatureReader.Source : "Unavailable",
                TemperatureCapturedAtUtc = temperature.HasValue ? DateTimeOffset.UtcNow : null
            };
        }).ToArray();
    }

    internal static IReadOnlyList<GpuAdapterReading> ApplyTemperatureFallback(IReadOnlyList<GpuAdapterReading> adapters,
        GpuAdapterReading? fallback, DateTimeOffset now)
    {
        if (fallback?.TemperatureCelsius == null || fallback.TemperatureCapturedAtUtc is not { } captured ||
            now - captured > TimeSpan.FromSeconds(5) || captured > now)
            return adapters;

        return adapters.Select(adapter => adapter.TemperatureCelsius == null && adapter.Id == fallback.Id
            ? adapter with
            {
                TemperatureCelsius = fallback.TemperatureCelsius,
                TemperatureSource = fallback.TemperatureSource,
                TemperatureCapturedAtUtc = captured
            }
            : adapter).ToArray();
    }

    [GeneratedRegex(@"^pid_\d+_luid_0x(?<high>[0-9a-f]{1,8})_0x(?<low>[0-9a-f]{1,8})_phys_(?<physical>\d+)_eng_(?<engine>\d+)_engtype_.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EngineInstance();

    internal static IReadOnlyList<GpuAdapterReading> BuildReadings(IReadOnlyList<GpuAdapterIdentity> inventory,
        IEnumerable<(string Name, double Usage)> samples, DateTimeOffset capturedAtUtc)
    {
        var totals = new Dictionary<(ulong Luid, int Physical, int Engine), double>();
        var softwareAdapters = inventory.Where(adapter => !adapter.IsHardware).Select(adapter => adapter.Luid).ToHashSet();
        foreach (var (name, usage) in samples)
        {
            if (!double.IsFinite(usage) || usage is < 0 or > 100) continue;
            var match = EngineInstance().Match(name);
            if (!match.Success ||
                !int.TryParse(match.Groups["physical"].ValueSpan, out var physical) ||
                !int.TryParse(match.Groups["engine"].ValueSpan, out var engine)) continue;
            ulong luid = (ulong.Parse(match.Groups["high"].ValueSpan, NumberStyles.HexNumber) << 32)
                       | uint.Parse(match.Groups["low"].ValueSpan, NumberStyles.HexNumber);
            if (softwareAdapters.Contains(luid)) continue;
            var key = (luid, physical, engine);
            totals[key] = totals.GetValueOrDefault(key) + usage;
        }

        // Each process contributes to its physical engine. Independent engines can run concurrently.
        var busiest = totals.GroupBy(row => row.Key.Luid)
            .ToDictionary(group => group.Key, group => Math.Round(Math.Min(100, group.Max(row => row.Value)), 1));
        var names = inventory.Where(adapter => adapter.IsHardware).ToDictionary(adapter => adapter.Luid);
        return names.Keys.Union(busiest.Keys).Order()
            .Select((luid, index) =>
            {
                names.TryGetValue(luid, out var adapter);
                var measured = busiest.TryGetValue(luid, out var usage);
                return new GpuAdapterReading(luid.ToString("x16"), adapter?.Name ?? $"Graphics adapter {index + 1}",
                    adapter?.Type ?? "Unknown", measured ? usage : null, measured ? capturedAtUtc : null);
            }).ToArray();
    }
}
