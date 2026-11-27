using System.ComponentModel;
using System.Text.Json;

namespace RigMD.Api.Services;

public sealed record SystemInventoryField(string Label, string Value);
public sealed record SystemInventoryEntry(string Id, string Name, string Identifier, string Source,
    string? State, string? Description, IReadOnlyList<SystemInventoryField> Fields);
public sealed record SystemInventorySnapshot(IReadOnlyList<SystemInventoryEntry> Entries,
    IReadOnlyList<string> UnavailableSources, IReadOnlyList<string> LimitedSources);

internal static class WindowsSystemInventory
{
    private const int LimitPerSource = 2048;

    private sealed record Source(string Label, string Query, string Identifier, string DisplayName,
        (string Property, string Label)[] Fields);
    private sealed record SourceReading(Source Source, IReadOnlyList<SystemInventoryEntry> Entries, bool Unavailable, bool Limited);

    private static readonly Source[] DriverSources =
    [
        new("Device drivers", "Get-CimInstance -ClassName Win32_PnPSignedDriver | Select-Object DeviceID,DeviceName,Description,DriverProviderName,DriverVersion,InfName,IsSigned,Signer",
            "DeviceID", "DeviceName",
            [("DriverProviderName", "Provider"), ("DriverVersion", "Version"), ("InfName", "INF"), ("IsSigned", "Signature"), ("Signer", "Signer")]),
        new("Driver services", "Get-CimInstance -ClassName Win32_SystemDriver | Select-Object Name,DisplayName,Description,State,StartMode,ServiceType",
            "Name", "DisplayName", [("StartMode", "Startup"), ("ServiceType", "Service type")]),
        new("Driver Store packages", """
            Get-WindowsDriver -Online -All | Select-Object Driver,ClassName,ProviderName,Inbox,
                @{Name='Version';Expression={[string]$_.Version}},
                @{Name='OriginalInf';Expression={if ($_.OriginalFileName) {[IO.Path]::GetFileName($_.OriginalFileName)}}}
            """, "Driver", "OriginalInf",
            [("ProviderName", "Provider"), ("Version", "Version"), ("ClassName", "Class"), ("OriginalInf", "Original INF"), ("Inbox", "Included with Windows")])
    ];

    private static readonly Source[] ServiceSources =
    [
        new("Windows services", "Get-CimInstance -ClassName Win32_Service | Select-Object Name,DisplayName,Description,State,StartMode,ServiceType",
            "Name", "DisplayName", [("StartMode", "Startup"), ("ServiceType", "Service type")])
    ];

    public static Task<SystemInventorySnapshot> ReadDriversAsync(CancellationToken token) => ReadAsync(DriverSources, token);
    public static Task<SystemInventorySnapshot> ReadServicesAsync(CancellationToken token) => ReadAsync(ServiceSources, token);

    private static async Task<SystemInventorySnapshot> ReadAsync(Source[] sources, CancellationToken token)
    {
        var readings = await Task.WhenAll(sources.Select(source => ReadSourceAsync(source, token)));
        return new(readings.SelectMany(reading => reading.Entries)
                .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            readings.Where(reading => reading.Unavailable).Select(reading => reading.Source.Label).ToArray(),
            readings.Where(reading => reading.Limited).Select(reading => reading.Source.Label).ToArray());
    }

    private static async Task<SourceReading> ReadSourceAsync(Source source, CancellationToken token)
    {
        // Queries and selected properties are fixed. Inventory never receives process names or commands from the caller.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $rows = @({{source.Query}} | Select-Object -First {{LimitPerSource + 1}})
            ConvertTo-Json -InputObject $rows -Compress
            """;
        try
        {
            var (code, output) = await DeviceDiagnosticRunner.RunPowerShellAsync(script, token);
            if (code != 0) return new(source, [], Unavailable: true, Limited: false);
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement;
            if (rows.ValueKind != JsonValueKind.Array) return new(source, [], Unavailable: true, Limited: false);
            var entries = rows.EnumerateArray().Take(LimitPerSource).Select(row => ReadEntry(row, source.Label,
                source.Identifier, source.DisplayName, source.Fields)).OfType<SystemInventoryEntry>().ToArray();
            return new(source, entries, Unavailable: false, Limited: rows.GetArrayLength() > LimitPerSource);
        }
        catch (Exception error) when (!token.IsCancellationRequested && error is
            OperationCanceledException or IOException or InvalidOperationException or UnauthorizedAccessException or Win32Exception or JsonException)
        {
            return new(source, [], Unavailable: true, Limited: false);
        }
    }

    internal static SystemInventoryEntry? ReadEntry(JsonElement row, string source, string identifierProperty,
        string displayProperty, (string Property, string Label)[] fields)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;

        string? Text(string property, int limit = 240)
        {
            if (!row.TryGetProperty(property, out var field) || field.ValueKind != JsonValueKind.String) return null;
            var value = new string((field.GetString() ?? "").Take(limit)
                .Select(character => char.IsControl(character) ? ' ' : character).ToArray()).Trim();
            return value.Length == 0 ? null : value;
        }

        var identifier = Text(identifierProperty, 600);
        if (identifier is null) return null;
        var details = new List<SystemInventoryField>();
        foreach (var (property, label) in fields)
        {
            var value = row.TryGetProperty(property, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property == "IsSigned" ? field.GetBoolean() ? "Signed" : "Unsigned" : field.GetBoolean() ? "Yes" : "No"
                : Text(property);
            if (value is not null) details.Add(new(label, value));
        }
        var description = Text("Description", 800);
        var name = Text(displayProperty) ?? description ?? identifier;
        return new(source + "/" + identifier, name, identifier, source, Text("State"), description, details);
    }
}
