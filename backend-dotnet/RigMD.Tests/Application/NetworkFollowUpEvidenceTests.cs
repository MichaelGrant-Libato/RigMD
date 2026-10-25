using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class NetworkFollowUpEvidenceTests
{
    private static HardwareProfileDto Profile(bool connected) => new()
    {
        Network = new()
        {
            QuerySucceeded = true,
            AdapterName = connected ? "Wi-Fi" : "vEthernet (WSL (Hyper-V firewall))",
            HasActiveAdapter = true,
            HasIpv4Address = true,
            HasDefaultGateway = connected,
            HasDnsServers = true,
            DnsTestHost = "example.com",
            DnsResolutionSucceeded = connected,
            DnsResolutionMessage = connected ? "Lookup succeeded." : "Lookup failed.",
            PingLatencyMs = connected ? 15 : null,
            PacketLossPercent = connected ? 0 : null,
            IsWifi = connected,
            WifiSignalStrength = connected ? 79 : 0
        }
    };

    private static AutomaticDiagnosisResult Diagnose(HardwareProfileDto hardware) =>
        new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = ["network"], Hardware = hardware
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdapterEvidenceNamesItsSourceWithoutCertifyingInternetAccess(bool connected)
    {
        var result = Diagnose(Profile(connected));
        var adapter = Assert.Single(result.Proof, p => p.MetricId == "network_adapter");
        Assert.Equal("Reported", adapter.Value);
        Assert.Contains(connected ? "Selected adapter: Wi-Fi." : "Selected adapter: vEthernet (WSL (Hyper-V firewall)).", adapter.Meaning);
        Assert.Contains(connected ? "does not verify internet access" : "No default IPv4 gateway was reported", adapter.Meaning);
        Assert.Contains("Selected adapter:", Assert.Single(result.Proof, p => p.MetricId == "network_address").Meaning);
        Assert.Contains("does not establish that they are reachable", Assert.Single(result.Proof, p => p.MetricId == "network_dns_config").Meaning);
        if (!connected)
        {
            Assert.Contains("local or virtual-network", adapter.Meaning);
            Assert.Equal("high", Assert.Single(result.Proof, p => p.MetricId == "network_gateway").Status);
            Assert.Equal("high", Assert.Single(result.Proof, p => p.MetricId == "network_dns").Status);
            Assert.All(result.Proof.Where(p => p.MetricId is "network_latency" or "network_loss"), p =>
            {
                Assert.Equal("unknown", p.Status);
                Assert.Equal("Unavailable", p.Value);
            });
        }
    }

    [Fact]
    public void DisconnectionAndRecoveryRecheckTheSameScopeWithoutMutatingTheBaseline()
    {
        var initial = Diagnose(Profile(true));
        var original = initial.Proof.Select(p => (object)new
        {
            metric_id = p.MetricId, label = p.Label, value = p.Value, status = p.Status, meaning = p.Meaning
        }).ToArray();
        var before = JsonSerializer.Serialize(original);
        var service = new ResolutionService();
        ResolutionResultDto Recheck(bool connected) => service.CheckResolution(initial.DiagnosedCategory, Profile(connected),
            componentIds: "[\"network\"]", diagnosisMode: "component", originalProof: original);

        var offline = Recheck(false);
        Assert.Equal("still_active", offline.resolution_status);
        var rows = JsonSerializer.SerializeToElement(offline.resolution_proof).EnumerateArray().ToArray();
        Assert.All(rows, p => Assert.StartsWith("network_", p.GetProperty("metric_id").GetString()!));
        Assert.Contains(rows, p => p.GetProperty("meaning").GetString()!.Contains("vEthernet (WSL"));
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "network_latency" && p.GetProperty("status").GetString() == "unknown");

        var recovered = Recheck(true);
        Assert.Equal("resolved", recovered.resolution_status);
        var recoveryRows = JsonSerializer.SerializeToElement(recovered.resolution_proof).EnumerateArray().ToArray();
        Assert.DoesNotContain(recoveryRows, p => p.GetProperty("status").GetString() is "high" or "unknown");
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }
}
