using System.Text.Json;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Application;

public class DnsCacheActionPolicyTests
{
    public static object[] Rows(string variant = "valid") {
        var rows = new List<object>();
        foreach (var id in new[] { "network_adapter", "network_address", "network_gateway", "network_dns_config", "network_dns" }) {
            if (variant == "missing" && id == "network_gateway") continue;
            var failed = id == "network_dns" || variant == id;
            rows.Add(new { metric_id = id, value = failed ? "Not reported" : "Reported", status = failed ? "high" : "normal",
                evidence_kind = variant == "unavailable" && id == "network_dns" ? "unavailable" : "measurement" });
        }
        if (variant == "duplicate") rows.Add(rows[^1]);
        if (variant == "latency-only") rows[^1] = new { metric_id = "network_dns", value = "Reported", status = "normal", evidence_kind = "measurement" };
        return rows.ToArray();
    }
    public static DiagnosticOutput Diagnostic(string variant = "valid") => new() {
        Id = Guid.NewGuid(), DiagnosticSessionId = Guid.NewGuid(), DiagnosedCategory = "Network issue", ActionCategory = "Troubleshoot",
        Session = new() { Answers = new List<SessionAnswer> {
            new() { QuestionKey = "diagnosis_mode", AnswerValue = "scenario" },
            new() { QuestionKey = "scenario_id", AnswerValue = "network-problem" },
            new() { QuestionKey = "proof", AnswerValue = JsonSerializer.Serialize(Rows(variant)) }
        } }
    };
    public static HardwareProfileDto Hardware() => new() { Network = new() {
        QuerySucceeded = true, HasActiveAdapter = true, HasIpv4Address = true, HasDefaultGateway = true, HasDnsServers = true,
        DnsResolutionSucceeded = false, DnsTestHost = "example.com", DnsResolutionMessage = "DNS resolution failed: test fixture."
    } };
    [Fact]
    public void SavedAndCurrentDnsFailureWithConnectionAllowsReview() => Assert.Null(DnsCacheActionPolicy.BlockReason(Diagnostic(), Hardware()));
    [Theory]
    [InlineData("network_adapter")][InlineData("network_address")][InlineData("network_gateway")][InlineData("network_dns_config")]
    [InlineData("missing")][InlineData("duplicate")][InlineData("unavailable")][InlineData("latency-only")]
    public async Task UnsupportedSavedEvidenceCannotProduceFallbackActionOrExecute(string variant) {
        var diagnostic = Diagnostic(variant);
        var hardware = Hardware();
        Assert.NotNull(DnsCacheActionPolicy.BlockReason(diagnostic, hardware));
        var executor = new NeverExecute();
        var orchestrator = new AutonomousOrchestrator(executor);
        var preview = await orchestrator.RunDryRunCycleAsync(diagnostic, hardware);
        Assert.Empty(preview.Plan!.PlannedActions);
        var execution = await orchestrator.RunExecutionCycleAsync(diagnostic, hardware, true, requestedToolName: "flush_dns_cache");
        Assert.Empty(execution.Attempts);
        Assert.Equal(0, executor.Calls);
    }
    [Theory]
    [InlineData("adapter")][InlineData("address")][InlineData("gateway")][InlineData("servers")]
    [InlineData("query")][InlineData("lookup-cleared")][InlineData("lookup-unavailable")]
    public void ChangedOrUnavailableCurrentEvidenceBlocks(string changed) {
        var hardware = Hardware(); Change(hardware.Network, changed);
        Assert.NotNull(DnsCacheActionPolicy.BlockReason(Diagnostic(), hardware));
    }
    internal static void Change(NetworkStatsDto network, string changed) {
        switch (changed) {
            case "adapter": network.HasActiveAdapter = false; break;
            case "address": network.HasIpv4Address = false; break;
            case "gateway": network.HasDefaultGateway = false; break;
            case "servers": network.HasDnsServers = false; break;
            case "query": network.QuerySucceeded = false; break;
            case "lookup-cleared": network.DnsResolutionSucceeded = true; break;
            case "lookup-unavailable": network.DnsResolutionMessage = ""; break;
        }
    }
    [Theory]
    [InlineData("[broken")][InlineData("null")][InlineData("{}")][InlineData("[7]")]
    public void MalformedLatestEvidenceCannotFallBackToOldWarning(string json) {
        var diagnostic = Diagnostic();
        diagnostic.Session!.Answers.Add(new() { QuestionKey = "resolution_proof", AnswerValue = json });
        Assert.NotNull(DnsCacheActionPolicy.BlockReason(diagnostic, Hardware()));
    }
    private sealed class NeverExecute : IRemediationExecutor {
        public int Calls;
        public Task<ExecutionResult> ExecuteAsync(RemediationActionDef action, Action<string>? progressReporter = null) {
            Calls++; throw new InvalidOperationException("No operating-system action is permitted in this test.");
        }
    }
}
