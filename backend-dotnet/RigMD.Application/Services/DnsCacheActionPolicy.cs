using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Application.Services;

public static class DnsCacheActionPolicy
{
    public const string ConnectionGuidance = "Reconnect Wi-Fi or Ethernet, then use Check again. Refreshing saved website addresses cannot restore a disconnected adapter or missing network configuration.";
    public const string EvidenceGuidance = "This check does not contain a verified website-address lookup failure with an active IPv4 connection, gateway and configured DNS servers. Check the current network status before choosing an action.";
    public static bool IsDnsTool(string? tool) => tool is not null &&
        (tool.Equals("flush_dns_cache", StringComparison.OrdinalIgnoreCase) || tool.Equals("flush_dns", StringComparison.OrdinalIgnoreCase));
    public static bool IsNetworkCategory(string? category) => category is not null &&
        (category.Contains("network", StringComparison.OrdinalIgnoreCase) || category.Contains("dns", StringComparison.OrdinalIgnoreCase) || category.Contains("internet", StringComparison.OrdinalIgnoreCase));

    public static string? BlockReason(DiagnosticOutput diagnostic, HardwareProfileDto hardware)
    {
        string? Answer(string key) => diagnostic.Session?.Answers.FirstOrDefault(a => a.QuestionKey == key)?.AnswerValue;
        var json = Answer("resolution_proof");
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "[]") json = Answer("proof");
        try
        {
            using var document = JsonDocument.Parse(json ?? "null");
            if (document.RootElement.ValueKind != JsonValueKind.Array) return EvidenceGuidance;
            var rows = document.RootElement.EnumerateArray().ToArray();
            string Text(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var value) &&
                value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            bool Single(string id, bool normal) {
                var matches = rows.Where(row => Text(row, "metric_id") == id).ToArray();
                if (matches.Length != 1) return false;
                var row = matches[0];
                if (Text(row, "evidence_kind") is "unavailable" or "missing_follow_up") return false;
                return normal ? Text(row, "status") is "normal" or "normal now" && Text(row, "value") == "Reported"
                    : Text(row, "status") is "high" or "elevated" or "warning" or "critical" or "still needs attention" &&
                      Text(row, "value") is "Not reported" or "Failed" or "Test did not pass";
            }
            if (!Single("network_adapter", true) || !Single("network_address", true) ||
                !Single("network_gateway", true) || !Single("network_dns_config", true)) return ConnectionGuidance;
            if (!Single("network_dns", false)) return EvidenceGuidance;
        }
        catch (JsonException) { return EvidenceGuidance; }
        var network = hardware.Network;
        if (network?.QuerySucceeded != true)
            return "The current network reading is unavailable. No DNS-cache action was run. Check again before choosing an action.";
        if (!network.HasActiveAdapter || !network.HasIpv4Address || !network.HasDefaultGateway || !network.HasDnsServers)
            return ConnectionGuidance;
        if (string.IsNullOrWhiteSpace(network.DnsTestHost) || string.IsNullOrWhiteSpace(network.DnsResolutionMessage))
            return "A current website-address lookup result is unavailable. No DNS-cache action was run. Check again before choosing an action.";
        return network.DnsResolutionSucceeded
            ? "The current website-address lookup succeeded. No DNS-cache action is indicated by this reading. Check again if the problem returns."
            : null;
    }
}
