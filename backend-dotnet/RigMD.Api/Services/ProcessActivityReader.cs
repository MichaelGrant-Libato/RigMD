using System.Diagnostics;

namespace RigMD.Api.Services;

public sealed record AppActivityRow(string Name, int Processes, double? CpuPercent, long? WorkingSetBytes);
public sealed record AppActivityReading(DateTimeOffset CapturedAt, double Seconds, bool Partial,
    AppActivityRow[] ByCpu, AppActivityRow[] ByMemory);

public static class ProcessActivityReader
{
    private sealed record ProcessSample(string Name, DateTime StartedAt, double? CpuSeconds, long? WorkingSetBytes);

    public static async Task<AppActivityReading> CaptureAsync(CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        var (before, firstPartial) = Read(token);
        await Task.Delay(TimeSpan.FromMilliseconds(1200), token);
        var (after, secondPartial) = Read(token);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var rows = after.Select(pair =>
        {
            var item = pair.Value;
            double? cpu = before.TryGetValue(pair.Key, out var earlier) && earlier.StartedAt == item.StartedAt &&
                earlier.CpuSeconds is { } prior && item.CpuSeconds is { } current && current >= prior
                ? Math.Min(100, (current - prior) / elapsed / Environment.ProcessorCount * 100) : null;
            return (item.Name, Cpu: cpu, item.WorkingSetBytes);
        }).GroupBy(sample => sample.Name, StringComparer.OrdinalIgnoreCase).Select(group =>
            new AppActivityRow(group.Key, group.Count(),
                group.Any(row => row.Cpu.HasValue) ? Math.Min(100, group.Sum(row => row.Cpu ?? 0)) : null,
                group.Any(row => row.WorkingSetBytes.HasValue) ? group.Sum(row => row.WorkingSetBytes ?? 0) : null))
            .ToArray();
        return new(DateTimeOffset.UtcNow, elapsed, firstPartial || secondPartial ||
            rows.Any(row => row.CpuPercent is null || row.WorkingSetBytes is null),
            rows.Where(row => row.CpuPercent is > 0).OrderByDescending(row => row.CpuPercent).ThenBy(row => row.Name).Take(12).ToArray(),
            rows.Where(row => row.WorkingSetBytes is > 0).OrderByDescending(row => row.WorkingSetBytes).ThenBy(row => row.Name).Take(12).ToArray());
    }

    private static (Dictionary<int, ProcessSample> Rows, bool Partial) Read(CancellationToken token)
    {
        var rows = new Dictionary<int, ProcessSample>();
        var partial = false;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                token.ThrowIfCancellationRequested();
                if (process.Id is 0 or 4) continue;
                try
                {
                    var name = process.ProcessName;
                    var workingSet = process.WorkingSet64;
                    DateTime started = default;
                    double? cpu = null;
                    try
                    {
                        started = process.StartTime.ToUniversalTime();
                        cpu = process.TotalProcessorTime.TotalSeconds;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                    { partial = true; }
                    rows[process.Id] = new(name, started, cpu, workingSet >= 0 ? workingSet : null);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                { partial = true; }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return (rows, partial);
    }
}
