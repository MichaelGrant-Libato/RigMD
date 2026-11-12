using System;
using System.Text.Json;

namespace RigMD.Desktop;

public static class DesktopPerformanceMessage
{
    public static bool TryReadDashboardReady(string source, string json, out string outcome) => TryRead(source, json, "rigmd:dashboard-ready", out outcome);
    public static bool TryReadHomeSummaryReady(string source, string json, out string outcome) => TryRead(source, json, "rigmd:home-summary-ready", out outcome);

    private static bool TryRead(string source, string json, string expectedType, out string outcome)
    {
        outcome = string.Empty;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != "http://localhost:5273" || json.Length > 256) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != expectedType
                || !root.TryGetProperty("outcome", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() is not ("completed" or "incomplete")) return false;
            outcome = status.GetString()!;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
