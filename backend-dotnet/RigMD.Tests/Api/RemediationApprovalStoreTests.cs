using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public class RemediationApprovalStoreTests
{
    [Fact]
    public void ApprovalReturnsOnlyItsOwnServerContextOnce()
    {
        var store = new RemediationApprovalStore(TimeProvider.System); var context = new object();
        var approval = store.Issue(Scope(), context);
        Assert.True(store.TryConsume(approval.Token, Scope(), out var bound)); Assert.Same(context, bound);
        Assert.False(store.TryConsume(approval.Token, Scope(), out bound)); Assert.Null(bound);
        approval = store.Issue(Scope(), context);
        Assert.False(store.TryConsume(approval.Token, Scope() with { ToolName = "other" }, out bound)); Assert.Null(bound);
    }

    [Fact]
    public void ApprovalContextIsNeverIncludedInJson()
    {
        var preview = new RigMD.Application.Models.ToolDryRunPreview { ApprovalContext = new { PrivateFilePath = "secret-path" } };
        var json = System.Text.Json.JsonSerializer.Serialize(preview);
        Assert.DoesNotContain("secret-path", json); Assert.DoesNotContain("ApprovalContext", json);
    }
    private static readonly Guid SessionId = Guid.NewGuid();
    private static RemediationApprovalStore.Scope Scope() =>
        RemediationApprovalStore.CreateScope("remediation", SessionId, "clear_temp_files", "{\"target\":\"original\"}");

    [Fact]
    public void Approval_IsSingleUse()
    {
        var store = new RemediationApprovalStore(TimeProvider.System);
        var scope = Scope();
        var approval = store.Issue(scope);
        Assert.True(store.TryConsume(approval.Token, scope));
        Assert.False(store.TryConsume(approval.Token, scope));
    }

    [Fact]
    public void Approval_ExpiresAtFiveMinutes()
    {
        var clock = new TestClock();
        var store = new RemediationApprovalStore(clock);
        var scope = Scope();
        var approval = store.Issue(scope);
        clock.Now = approval.ExpiresAt;
        Assert.False(store.TryConsume(approval.Token, scope));
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("session")]
    [InlineData("tool")]
    [InlineData("arguments")]
    public void Approval_CannotBeReusedForChangedRequest(string changed)
    {
        var store = new RemediationApprovalStore(TimeProvider.System);
        var scope = Scope();
        var approval = store.Issue(scope);
        var altered = changed switch
        {
            "operation" => scope with { Operation = "close-app" },
            "session" => scope with { SessionId = Guid.NewGuid() },
            "tool" => scope with { ToolName = "terminate_processes" },
            _ => scope with { Arguments = "{\"target\":\"different\"}" }
        };
        Assert.False(store.TryConsume(approval.Token, altered));
        Assert.False(store.TryConsume(approval.Token, scope));
    }

    [Fact]
    public async Task ConcurrentConsumption_OnlyOneRequestCanWin()
    {
        var store = new RemediationApprovalStore(TimeProvider.System);
        var scope = Scope();
        var approval = store.Issue(scope);
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => store.TryConsume(approval.Token, scope))));
        Assert.Single(results, success => success);
    }

    [Fact]
    public void ServerRestart_DiscardsOutstandingApprovals()
    {
        var scope = Scope();
        var approval = new RemediationApprovalStore(TimeProvider.System).Issue(scope);
        Assert.False(new RemediationApprovalStore(TimeProvider.System).TryConsume(approval.Token, scope));
    }

    [Fact]
    public void EquivalentWhitespace_DoesNotChangeScope()
    {
        var expected = RemediationApprovalStore.CreateScope("remediation", SessionId, "tool", "{\"x\":1}");
        var actual = RemediationApprovalStore.CreateScope("remediation", SessionId, "tool", "{ \"x\" : 1 }");
        Assert.Equal(expected, actual);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
