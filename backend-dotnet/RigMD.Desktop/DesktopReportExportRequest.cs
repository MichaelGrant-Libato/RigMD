using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RigMD.Desktop;

public sealed record DesktopReportExportRequest(string RequestId, string FileName)
{
    public static bool TryParse(string source, string json, out DesktopReportExportRequest? request)
    {
        request = null;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var origin) ||
            origin.GetLeftPart(UriPartial.Authority) != "http://localhost:5273" ||
            json.Length > 1024)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) || type.GetString() != "rigmd:save-report-pdf" ||
                !root.TryGetProperty("requestId", out var id) || id.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("filename", out var name) || name.ValueKind != JsonValueKind.String)
                return false;

            var requestId = id.GetString()!;
            var fileName = name.GetString()!;
            if (!Regex.IsMatch(requestId, @"\A[a-zA-Z0-9_-]{1,80}\z") ||
                !Regex.IsMatch(fileName, @"\ARigMD-Device-Report-[a-zA-Z0-9_-]{1,150}\.pdf\z"))
                return false;

            request = new DesktopReportExportRequest(requestId, fileName);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
