using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class RemediationEligibilityPolicyTests
{
    internal static DiagnosticSessionDto Check(int minute, string status = "high", string value = "90%", string device = "device-a") => new()
    {
        SessionId = Guid.NewGuid().ToString(), DeviceId = device, ProfileId = "shared-cpu-os-profile",
        ClientId = "same-account", AgentId = "local", CreatedAt = At(minute),
        DiagnosisMode = "component", ComponentIds = "[\"memory\"]", DiagnosedCategory = "High Memory Pressure",
        ResolutionStatus = "open", Proof = [Row("memory_usage", status, value)]
    };

    internal static string At(int minute) => new DateTimeOffset(2026, 10, 25, 0, minute, 0, TimeSpan.Zero).ToString("o");
    internal static object Row(string metric, string status, string value, string kind = "measurement") => new
    {
        metric_id = metric, label = metric, status, value, evidence_kind = kind
    };

    [Theory]
    [InlineData("normal", "40%")]
    [InlineData("high", "95%")]
    public void NewerMatchingCheckSupersedesOlderWarningWithoutRewritingItsResolution(string status, string value)
    {
        var old = Check(0);
        var newer = Check(1, status, value);
        var before = JsonSerializer.Serialize(old);
        var result = RemediationEligibilityPolicy.Evaluate(old, [old, newer]);
        Assert.Equal("superseded", result.State);
        Assert.Equal(newer.SessionId, result.NewerSessionId);
        Assert.Equal(newer.CreatedAt, result.CheckedAt);
        Assert.Equal(before, JsonSerializer.Serialize(old));
    }

    [Theory]
    [InlineData("unknown", "Unavailable", "measurement")]
    [InlineData("normal", "Unavailable", "measurement")]
    [InlineData("normal", "40%", "")]
    [InlineData("normal", "40%", "unavailable")]
    public void NewerUnverifiedEvidenceRequiresCurrentCheck(string status, string value, string kind)
    {
        var old = Check(0);
        var newer = Check(1);
        newer.Proof = [Row("memory_usage", status, value, kind)];
        var result = RemediationEligibilityPolicy.Evaluate(old, [newer]);
        Assert.Equal("recheck_required", result.State);
        Assert.Null(result.NewerSessionId);
    }

    [Fact]
    public void DifferentComputerCannotClearWarningEvenWithSameAccountAgentAndCpuOsProfile()
    {
        var old = Check(0);
        Assert.Equal("allowed", RemediationEligibilityPolicy.Evaluate(old, [Check(1, "normal", "40%", "device-b")]).State);
    }

    [Fact]
    public void UnrelatedCheckCannotSupersedeWarningEvenIfItContainsAnIncidentalMemoryRow()
    {
        var old = Check(0);
        var cpu = Check(1, "normal", "40%");
        cpu.ComponentIds = "[\"cpu\"]";
        Assert.Equal("allowed", RemediationEligibilityPolicy.Evaluate(old, [cpu]).State);
    }

    [Fact]
    public void FullCheckCanSupersedeComponentWarningByMatchingMetric()
    {
        var old = Check(0);
        var full = Check(1, "normal", "40%"); full.DiagnosisMode = "full";
        Assert.Equal("superseded", RemediationEligibilityPolicy.Evaluate(old, [full]).State);
    }

    [Fact]
    public void LatestMatchingUnknownTakesPrecedenceOverEarlierClearReading()
    {
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(Check(0),
            [Check(1, "normal", "40%"), Check(2, "unknown", "Unavailable")]).State);
    }

    [Fact]
    public void MissingIdentityCannotBeInferredFromProfileClientOrLocalAgent()
    {
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(Check(0, device: ""), [Check(1)]).State);
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(Check(0), [Check(1, device: "")]).State);
    }

    [Fact]
    public void MissingProofAndUnknownLegacyScopeRequireCheck()
    {
        var legacy = Check(0); legacy.Proof = [];
        Assert.Equal("not_indicated", RemediationEligibilityPolicy.Evaluate(legacy, []).State);
        legacy = Check(0); legacy.DiagnosisMode = "";
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(legacy, []).State);
    }

    [Fact]
    public void FreshSameSessionFollowupCanAuthorizeExistingPathAfterEarlierClearCheck()
    {
        var old = Check(0);
        old.ResolutionCheckedAt = At(3); old.ResolutionStatus = "still_active";
        old.ResolutionProof = [Row("memory_usage", "high", "91%")];
        Assert.Equal("allowed", RemediationEligibilityPolicy.Evaluate(old, [Check(1, "normal", "40%")]).State);
    }

    [Fact]
    public void FreshFollowupIdentifiesLegacyCheckWithoutRewritingOriginalDeviceIdentity()
    {
        var old = Check(0, device: "");
        old.ResolutionCheckedAt = At(3); old.ResolutionStatus = "still_active";
        old.ResolutionDeviceId = "device-a";
        old.ResolutionProof = [Row("memory_usage", "high", "91%")];
        Assert.Equal("allowed", RemediationEligibilityPolicy.Evaluate(old, [Check(1, "normal", "40%")]).State);
        Assert.Equal("", old.DeviceId);
        Assert.Equal("superseded", RemediationEligibilityPolicy.Evaluate(Check(0), [old]).State);
    }

    [Theory]
    [InlineData("normal", "40%")]
    [InlineData("unknown", "Unavailable")]
    public void FollowupCannotFallBackToOldWarning(string status, string value)
    {
        var old = Check(0); old.ResolutionCheckedAt = At(3);
        old.ResolutionProof = [Row("memory_usage", status, value)];
        Assert.Equal(status == "normal" ? "not_indicated" : "recheck_required", RemediationEligibilityPolicy.Evaluate(old, []).State);
        old.ResolutionProof = [];
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(old, []).State);
    }

    [Fact]
    public void MatchingMeansMetricRatherThanCategoryOrNormalResolutionLabel()
    {
        var old = Check(0);
        var newer = Check(1); newer.ResolutionStatus = "resolved";
        newer.Proof = [Row("cpu_usage", "normal", "20%")];
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(old, [newer]).State);
    }

    [Fact]
    public void EqualCheckTimesCannotChooseAnArbitraryWinner()
    {
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(Check(0), [Check(0, "normal", "40%")]).State);
    }

    [Fact]
    public void ActionAwaitingRecheckAndInvalidTimestampsCannotReuseInitialWarning()
    {
        var current = Check(0); current.ResolutionStatus = "needs_recheck";
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(current, []).State);
        current = Check(0);
        var invalid = Check(1); invalid.CreatedAt = "not a timestamp";
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(current, [invalid]).State);
        invalid = Check(1); invalid.ResolutionCheckedAt = "not a timestamp";
        Assert.Equal("recheck_required", RemediationEligibilityPolicy.Evaluate(current, [invalid]).State);
    }

    [Theory]
    [InlineData("normal", "40%")]
    [InlineData("unknown", "Unavailable")]
    public void NoVerifiedWarningDoesNotInviteAnotherCheckEvenWhenIdentityIsMissing(string status, string value)
    {
        var current = Check(0, status, value, "");
        current.Proof = [Row("memory_usage", status, value), Row("cpu_usage", "unknown", "Unavailable")];
        Assert.Equal("not_indicated", RemediationEligibilityPolicy.Evaluate(current, []).State);
    }
}
