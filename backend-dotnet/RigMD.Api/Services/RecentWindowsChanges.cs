using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.Json;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Api.Services;

public sealed record WindowsChange(DateTimeOffset At, string Kind, string Detail);
public sealed record ChangeCoverage(string Source, string State);
public sealed record WindowsChanges(DateTimeOffset Since, DateTimeOffset CheckedAt, bool LimitedWindow,
    WindowsChange[] Changes, ChangeCoverage[] Coverage);
public sealed record RecordedCrashEvents(DateTimeOffset Since, DateTimeOffset Until, WindowsChange[] Events, ChangeCoverage Coverage);

public static class RecentWindowsChanges
{
    public static bool BelongsToThisDevice(string? id)
    {
        var device = LocalDiagnosticDeviceIdentity.Read();
        return device.Length > 0 && string.Equals(device, id, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<WindowsChanges> ReadAsync(DateTimeOffset savedAt, CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = savedAt < now.AddDays(-30) ? now.AddDays(-30) : savedAt;
        var stamp = cutoff.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var updates = ReadUpdatesAsync(cutoff, token);
        var driver = ReadEvents("Driver configuration", "Microsoft-Windows-Kernel-PnP/Configuration", "EventID=400",
            stamp, record => Describe(record, "Windows configured a device driver", "DriverName"), token);
        var crashes = ReadEvents("Application crashes", "Application",
            "Provider[@Name='Application Error'] and EventID=1000", stamp,
            record => Describe(record, "Windows recorded an application crash", "AppName"), token);
        var shutdowns = ReadEvents("Unexpected shutdowns", "System",
            "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001)",
            stamp, record => record.Id == 41 ? "Windows restarted without a clean shutdown (event 41)."
                : "Windows recorded a system crash (event 1001).", token);
        var sections = new[] { await updates, driver, crashes, shutdowns };
        return new(cutoff, now, cutoff > savedAt, sections.SelectMany(section => section.Events)
            .OrderByDescending(entry => entry.At).Take(100).ToArray(), sections.Select(section => section.Coverage).ToArray());
    }

    internal static (DateTimeOffset Start, DateTimeOffset End)? CrashWindow(object[] savedProof, bool application, DateTimeOffset now)
    {
        var metric = application ? "application_crash_history" : "system_crash_history";
        foreach (var row in savedProof.OfType<JsonElement>())
        {
            if (row.ValueKind == JsonValueKind.Object &&
                row.TryGetProperty("metric_id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == metric &&
                row.TryGetProperty("window_start", out var start) && start.ValueKind == JsonValueKind.String && start.TryGetDateTimeOffset(out var from) &&
                row.TryGetProperty("window_end", out var end) && end.ValueKind == JsonValueKind.String && end.TryGetDateTimeOffset(out var to) &&
                from < to && to <= now && to - from <= TimeSpan.FromDays(8))
                return (from, to);
        }
        return null;
    }

    internal static RecordedCrashEvents ReadCrashRecords(DateTimeOffset start, DateTimeOffset end, bool application, CancellationToken token)
    {
        var criterion = application
            ? "(EventID=1000 and Provider[@Name='Application Error']) or (EventID=1001 and Provider[@Name='Windows Error Reporting']) or (EventID=1002 and Provider[@Name='Application Hang']) or (EventID=1026 and Provider[@Name='.NET Runtime'])"
            : "(EventID=41 and Provider[@Name='Microsoft-Windows-Kernel-Power']) or (EventID=1001 and (Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck'])) or (EventID=6008 and Provider[@Name='EventLog'])";
        var log = application ? "Application" : "System";
        var section = ReadEvents(log, log, criterion, EventStamp(start),
            record => Describe(record, record.ProviderName ?? "Windows event", application ? "AppName" : "BugcheckCode"),
            token, until: EventStamp(end));
        return new(start, end, section.Events, section.Coverage);
    }

    private static string EventStamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static string Describe(EventRecord record, string label, string field)
    {
        var suffix = $" (event {record.Id}).";
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(record.ToXml());
            var value = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Data" &&
                element.Attribute("Name")?.Value == field)?.Value;
            if (string.IsNullOrWhiteSpace(value)) return label + suffix;
            var name = value.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 128 ? label + " · " + name + suffix : label + suffix;
        }
        catch (Exception ex) when (ex is EventLogException or System.Xml.XmlException) { return label + suffix; }
    }

    private static (WindowsChange[] Events, ChangeCoverage Coverage) ReadEvents(string kind, string log, string criterion,
        string stamp, Func<EventRecord, string> describe, CancellationToken token, string? until = null)
    {
        var entries = new List<WindowsChange>();
        try
        {
            var end = until == null ? "" : $" and @SystemTime <= '{until}'";
            var query = new EventLogQuery(log, PathType.LogName,
                $"*[System[({criterion}) and TimeCreated[@SystemTime >= '{stamp}'{end}]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            while (entries.Count < 25)
            {
                token.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(500));
                if (record is null) break;
                if (record.TimeCreated is { } time)
                    entries.Add(new(time.ToUniversalTime(), kind, describe(record)));
            }
            return (entries.ToArray(), new(kind, entries.Count == 25 ? "limited" : "checked"));
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        { return (entries.ToArray(), new(kind, entries.Count == 0 ? "unavailable" : "partial")); }
    }

    private static async Task<(WindowsChange[] Events, ChangeCoverage Coverage)> ReadUpdatesAsync(DateTimeOffset cutoff,
        CancellationToken token)
    {
        const string kind = "Windows updates";
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $since = [DateTimeOffset]::Parse('{{cutoff.ToString("o", CultureInfo.InvariantCulture)}}')
            $agent = (New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher()
            $count = [Math]::Min(256, $agent.GetTotalHistoryCount())
            $rows = @()
            if ($count -gt 0) {
                $rows = @($agent.QueryHistory(0, $count) | Where-Object { $_.Date.ToUniversalTime() -ge $since.UtcDateTime } |
                    Select-Object -First 25 | ForEach-Object {
                        $title = [string]$_.Title
                        [pscustomobject]@{ at = $_.Date.ToUniversalTime().ToString('o');
                            title = $title.Substring(0, [Math]::Min(200, $title.Length));
                            operation = [int]$_.Operation; result = [int]$_.ResultCode }
                    })
            }
            [pscustomobject]@{ limited = ($count -eq 256 -or $rows.Count -eq 25); rows = $rows } | ConvertTo-Json -Compress
            """;
        try
        {
            var (code, output) = await DeviceDiagnosticRunner.RunPowerShellAsync(script, token);
            if (code != 0) return ([], new(kind, "unavailable"));
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var entries = root.GetProperty("rows").EnumerateArray().Where(row =>
                row.GetProperty("at").TryGetDateTimeOffset(out _)).Select(row =>
            {
                var operation = row.GetProperty("operation").GetInt32() == 2 ? "Uninstall" : "Install";
                var result = row.GetProperty("result").GetInt32() switch
                {
                    2 => "succeeded", 3 => "succeeded with errors", 4 => "failed", 5 => "canceled", _ => "recorded"
                };
                return new WindowsChange(row.GetProperty("at").GetDateTimeOffset(), kind,
                    $"{operation} {result} · {row.GetProperty("title").GetString()}");
            }).ToArray();
            return (entries, new(kind, root.GetProperty("limited").GetBoolean() ? "limited" : "checked"));
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
            ex is JsonException or InvalidOperationException or KeyNotFoundException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { return ([], new(kind, "unavailable")); }
    }
}
