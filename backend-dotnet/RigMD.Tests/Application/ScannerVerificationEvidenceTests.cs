using System.Net.NetworkInformation;
using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;

namespace RigMD.Tests.Application;

public class ScannerVerificationEvidenceTests
{
    [Theory]
    [InlineData(IPStatus.Success, 0L, 0L)]
    [InlineData(IPStatus.Success, 24L, 24L)]
    [InlineData(IPStatus.TimedOut, 24, null)]
    [InlineData(IPStatus.DestinationHostUnreachable, 24, null)]
    [InlineData(IPStatus.Success, -1, null)]
    public void FailedLivePingCannotReuseAnEarlierLatency(IPStatus status, long latency, long? expected) =>
        Assert.Equal(expected, InspectNetworkConnectivityTool.MeasuredPingLatency(status, latency));

    [Fact]
    public void CompleteNetworkEvidenceIsRequiredBeforeTheTargetCanBeClear()
    {
        var data = new Dictionary<string, object?>
        {
            ["hasActiveAdapter"] = true, ["hasIpv4Address"] = true, ["hasDefaultGateway"] = true,
            ["hasDnsServers"] = true, ["packetLossPercent"] = 0d,
            ["liveProbe"] = new { dnsResolutionSucceeded = true, pingLatencyMs = 20, pingStatus = "Success" }
        };
        Assert.Equal(VerificationStatus.Resolved, AutonomousOrchestrator.EvaluateNetworkEvidence(JsonSerializer.Serialize(data)));
        data.Remove("packetLossPercent");
        Assert.Equal(VerificationStatus.Unknown, AutonomousOrchestrator.EvaluateNetworkEvidence(JsonSerializer.Serialize(data)));
        data["hasDefaultGateway"] = false;
        Assert.Equal(VerificationStatus.Unresolved, AutonomousOrchestrator.EvaluateNetworkEvidence(JsonSerializer.Serialize(data)));
    }

    [Theory]
    [InlineData(149, 0, VerificationStatus.Resolved)]
    [InlineData(150, 0, VerificationStatus.Unresolved)]
    [InlineData(20, 5, VerificationStatus.Unresolved)]
    [InlineData(20, -1, VerificationStatus.Unknown)]
    public void NetworkVerificationUsesTheSameReviewBoundaries(int latency, double loss, VerificationStatus expected)
    {
        var json = JsonSerializer.Serialize(new { hasActiveAdapter = true, hasIpv4Address = true,
            hasDefaultGateway = true, hasDnsServers = true, packetLossPercent = loss,
            liveProbe = new { dnsResolutionSucceeded = true, pingLatencyMs = latency, pingStatus = "Success" } });
        Assert.Equal(expected, AutonomousOrchestrator.EvaluateNetworkEvidence(json));
    }

    [Theory]
    [InlineData("{\"drives\":[{}]}", VerificationStatus.Unknown)]
    [InlineData("{\"drives\":[{\"isFailingSmart\":true}]}", VerificationStatus.Unresolved)]
    [InlineData("{\"volumes\":[{\"usagePercent\":25}]}", VerificationStatus.Resolved)]
    [InlineData("{\"volumes\":[{\"usagePercent\":80}]}", VerificationStatus.Unresolved)]
    [InlineData("{\"volumes\":[{\"usagePercent\":25},{}]}", VerificationStatus.Unknown)]
    [InlineData("{\"volumes\":[{\"usagePercent\":-5}]}", VerificationStatus.Unknown)]
    public void ADriveObjectAloneDoesNotVerifySpaceOrHealth(string json, VerificationStatus expected) =>
        Assert.Equal(expected, AutonomousOrchestrator.EvaluateStorageEvidence(json));

    [Fact]
    public void ChargeAndCoolTemperatureSnapshotsAreNotDrainOrCoolingTests()
    {
        Assert.Equal(VerificationStatus.Unknown, AutonomousOrchestrator.EvaluateBatteryEvidence(
            "{\"hasBattery\":true,\"battery\":{\"chargePercent\":80,\"healthStatus\":\"Good\"}}", false));
        Assert.Equal(VerificationStatus.Unknown, AutonomousOrchestrator.EvaluateBatteryEvidence("{\"hasBattery\":false}", false));
        Assert.Equal(VerificationStatus.Resolved, AutonomousOrchestrator.EvaluateBatteryEvidence("{\"hasBattery\":true,\"battery\":{\"chargePercent\":80}}", true));
        Assert.Equal(VerificationStatus.Unknown, AutonomousOrchestrator.EvaluateThermalEvidence("{\"temperatureCelsius\":40,\"isThermallyThrottling\":false}"));
        Assert.Equal(VerificationStatus.Unresolved, AutonomousOrchestrator.EvaluateThermalEvidence("{\"temperatureCelsius\":85}"));
        Assert.Equal(VerificationStatus.Unknown, AutonomousOrchestrator.EvaluateThermalEvidence("{\"temperatureCelsius\":-1}"));
    }
}
