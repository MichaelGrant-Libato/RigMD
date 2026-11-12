using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Infrastructure.Remediation.Actions;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Tests.Infrastructure;

public sealed class ExplorerRestartSafetyTests
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Platform : IExplorerRestartPlatform
    {
        public ExplorerContext User = new(2, "test-current-user", false);
        public ExplorerIdentity Original = new(100, 1000, 2, "test-current-user", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        public ExplorerIdentity? Shell;
        public bool Alive = true, StopAllowed = true, StartFails, AutoRestart = true, CancelAfterStop;
        public int Stops, Starts;
        public Platform() => Shell = Original;
        public ExplorerContext Context() => User;
        public ExplorerIdentity? ReadShell() => Shell;
        public bool IsRunning(ExplorerIdentity identity) => identity == Original && Alive;
        public Task<bool> StopAsync(ExplorerIdentity identity, CancellationToken cancellation)
        {
            Assert.Equal(Original, identity); Stops++;
            if (!StopAllowed) return Task.FromResult(false);
            Alive = false; Shell = AutoRestart ? Original with { Pid = 200, StartedUtcTicks = 2000 } : null;
            if (CancelAfterStop) throw new OperationCanceledException();
            return Task.FromResult(true);
        }
        public void StartShell()
        { Starts++; if (StartFails) throw new InvalidOperationException("Synthetic launch failure"); Shell = Original with { Pid = 300, StartedUtcTicks = 3000 }; }
        public Task PauseAsync(CancellationToken cancellation) => Task.CompletedTask;
    }
    private readonly Clock _clock = new();
    private readonly Platform _platform = new();
    private RestartWindowsExplorerTool Tool() => new(NullLoggerFactory.Instance, _platform, _clock);
    private static JsonElement Args(string json = "{}") { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private async Task<RestartWindowsExplorerTool> Approved()
    {
        var preview = await Tool().PreviewImpactAsync(Args()); Assert.True(preview.CanExecute, preview.WhatWillHappen);
        var tool = Tool(); Assert.True(tool.TryBindApprovalContext(preview.ApprovalContext!)); return tool;
    }
    [Fact]
    public async Task OriginalSurvivingFailedStopIsNotAReplacementShell()
    {
        var tool = await Approved(); _platform.StopAllowed = false;
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.Equal(_platform.Original, _platform.Shell);
        Assert.Equal(1, _platform.Stops); Assert.Equal(0, _platform.Starts);
    }
    [Fact]
    public async Task NewVerifiedShellSucceedsWithoutLaunchingAnExtraExplorer()
    {
        var tool = await Approved(); var result = await tool.ExecuteAsync(Args());
        Assert.True(result.Success, result.Summary); Assert.Equal(1, _platform.Stops); Assert.Equal(0, _platform.Starts);
        Assert.NotEqual(_platform.Original, _platform.Shell); Assert.False((await tool.ExecuteAsync(Args())).Success);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task MissingReplacementIsRecoveredOrReportedUnverified(bool fails)
    {
        var tool = await Approved(); _platform.AutoRestart = false; _platform.StartFails = fails;
        var result = await tool.ExecuteAsync(Args()); Assert.Equal(!fails, result.Success); Assert.Equal(1, _platform.Starts);
    }
    [Fact]
    public async Task CanceledStopStillChecksShellRecoveryWithoutClaimingSuccess()
    {
        var tool = await Approved(); _platform.CancelAfterStop = true; _platform.AutoRestart = false;
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.Equal(1, _platform.Starts); Assert.NotNull(_platform.Shell);
    }
    [Theory]
    [InlineData("session")] [InlineData("owner")] [InlineData("path")] [InlineData("admin")]
    public async Task OtherSessionsAccountsExecutablesAndElevatedContextsCannotBeTargeted(string mismatch)
    {
        switch (mismatch)
        {
            case "session": _platform.Shell = _platform.Original with { SessionId = 3 }; break;
            case "owner": _platform.Shell = _platform.Original with { UserSid = "different-user" }; break;
            case "path": _platform.Shell = _platform.Original with { Executable = Path.Combine(Path.GetTempPath(), "explorer.exe") }; break;
            case "admin": _platform.User = _platform.User with { Elevated = true }; break;
        }
        Assert.False((await Tool().PreviewImpactAsync(Args())).CanExecute); Assert.False((await Tool().ExecuteAsync(Args())).Success);
        Assert.Equal(0, _platform.Stops); Assert.Equal(0, _platform.Starts);
    }
    [Fact]
    public async Task ChangedShellAndExpiredApprovalDoNotStopAnyProcess()
    {
        var tool = await Approved(); _platform.Shell = _platform.Original with { Pid = 400 };
        Assert.False((await tool.ExecuteAsync(Args())).Success); Assert.Equal(0, _platform.Stops);
        _platform.Shell = _platform.Original; tool = await Approved(); _clock.Now = _clock.Now.AddMinutes(6);
        Assert.False((await tool.ExecuteAsync(Args())).Success); Assert.Equal(0, _platform.Stops);
    }
    [Fact]
    public async Task DirectArgumentsAndLegacyDispatchCannotBypassApproval()
    {
        Assert.False((await Tool().PreviewImpactAsync(Args("{\"pid\":100}"))).CanExecute);
        Assert.False((await new RestartExplorerAction(NullLogger<RestartExplorerAction>.Instance).ExecuteAsync()).Success);
        Assert.Equal(0, _platform.Stops);
    }
}
