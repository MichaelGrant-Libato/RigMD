using RigMD.Application.Models;

namespace RigMD.Application.Services;

internal static class NetworkEvidence
{
    internal const double LatencyWarningMs = 150;
    internal const double LossWarningPercent = 5;
    public static List<AutomaticDiagnosisProof> Read(NetworkStatsDto? net)
    {
        var proof = new List<AutomaticDiagnosisProof>();
        void Add(string id, string label, string value, string status, string meaning) => proof.Add(new()
        { MetricId = id, Label = label, Value = value, Status = status, Meaning = meaning });
        if (net?.QuerySucceeded != true)
        {
            Add("network_connectivity", "Network checks", "Unavailable", "unknown", "Network collection did not complete. No connection state was inferred.");
            return proof;
        }
        void Flag(string id, string label, bool good, string detail) =>
            Add(id, label, good ? "Reported" : "Not reported", good ? "normal" : "high", detail);
        var adapterSource = string.IsNullOrWhiteSpace(net.AdapterName)
            ? "The selected adapter name was not reported."
            : $"Selected adapter: {net.AdapterName}.";
        var routeContext = net.HasDefaultGateway
            ? "A default IPv4 gateway is configured on this adapter; that alone does not verify internet access."
            : "No default IPv4 gateway was reported on this adapter; it may provide only a local or virtual-network connection.";
        Flag("network_adapter", "Active IPv4 adapter", net.HasActiveAdapter,
            net.HasActiveAdapter
                ? $"{adapterSource} Windows reported an operationally up adapter with an IPv4 address. {routeContext} This check does not certify all IPv6, VPN or local-only connections."
                : "No operationally up adapter with an IPv4 address was reported. This does not certify IPv6, VPN or local-only connectivity.");
        if (!net.HasActiveAdapter) return proof;
        Flag("network_address", "IPv4 address", net.HasIpv4Address, $"{adapterSource} An address is configuration evidence, not an internet-access test.");
        Flag("network_gateway", "Default IPv4 gateway", net.HasDefaultGateway, "A missing default route may prevent access outside the local network.");
        Flag("network_dns_config", "DNS server configuration", net.HasDnsServers, $"{adapterSource} Configured address-lookup servers are checked separately from a live lookup; their presence does not establish that they are reachable.");
        Flag("network_dns", "Website address lookup", net.DnsResolutionSucceeded, $"Target: {net.DnsTestHost}. {net.DnsResolutionMessage}");
        Add("network_latency", "Connection test latency", net.PingLatencyMs is >= 0 ? $"{net.PingLatencyMs} ms" : "Unavailable",
            net.PingLatencyMs is null or < 0 ? "unknown" : net.PingLatencyMs >= LatencyWarningMs ? "high" : "normal",
            "Average successful ICMP replies to 8.8.8.8. RigMD flags 150 ms for review; one target does not represent every service.");
        var loss = net.PacketLossPercent;
        var valid = loss.HasValue && double.IsFinite(loss.Value) && loss.Value is >= 0 and <= 100;
        Add("network_loss", "Connection test replies lost", valid ? $"{loss:0.#}%" : "Unavailable",
            !valid ? "unknown" : loss >= LossWarningPercent ? "high" : "normal",
            "Four ICMP requests to 8.8.8.8. Missing replies may reflect filtering or an unreachable target; this alone does not prove an internet outage.");
        if (net.IsWifi)
            Add("network_signal", "Wi-Fi signal", net.WifiSignalStrength is > 0 and <= 100 ? $"{net.WifiSignalStrength}%" : "Unavailable",
                net.WifiSignalStrength is <= 0 or > 100 ? "unknown" : net.WifiSignalStrength <= 45 ? "high" : "normal",
                "A signal snapshot, not measured throughput. Zero is treated as unavailable because this provider has no valid zero-signal indicator.");
        return proof;
    }
}
