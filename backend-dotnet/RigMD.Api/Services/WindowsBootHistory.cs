using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RigMD.Api.Services;

public sealed record BootRecord(DateTimeOffset RecordedAt, long DurationMs, long? MainPathMs, long? PostBootMs);
public sealed record BootHistoryReading(string State, IReadOnlyList<BootRecord> Records);

internal static class WindowsBootHistory
{
    private const string LogName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    public static BootHistoryReading Read(CancellationToken token)
    {
        var records = new List<BootRecord>();
        try
        {
            var query = new EventLogQuery(LogName, PathType.LogName, "*[System[EventID=100]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var inspected = 0; inspected < 15 && records.Count < 5; inspected++)
            {
                token.ThrowIfCancellationRequested();
                using var record = reader.ReadEvent(TimeSpan.FromMilliseconds(500));
                if (record is null) break;
                if (record.TimeCreated is { } time && ReadRecord(record.ToXml(), time.ToUniversalTime()) is { } row)
                    records.Add(row);
            }
            return new(records.Count > 0 ? "measured" : "no records", records);
        }
        catch (UnauthorizedAccessException) { return new(records.Count > 0 ? "partial" : "permission denied", records); }
        catch (EventLogException) { return new(records.Count > 0 ? "partial" : "unavailable", records); }
    }

    internal static BootRecord? ReadRecord(string xml, DateTimeOffset recordedAt)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            if (root is null) return null;
            var ns = root.Name.Namespace;
            var data = root.Element(ns + "EventData")?.Elements(ns + "Data").ToArray() ?? [];
            long? Value(string name) => long.TryParse(data.FirstOrDefault(node => (string?)node.Attribute("Name") == name)?.Value,
                NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
            return Value("BootTime") is > 0 and var duration
                ? new(recordedAt, duration, Value("MainPathBootTime"), Value("BootPostBootTime"))
                : null;
        }
        catch (XmlException) { return null; }
    }
}
