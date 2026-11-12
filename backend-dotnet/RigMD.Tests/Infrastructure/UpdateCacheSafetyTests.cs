using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Tests.Infrastructure;

public sealed class UpdateCacheSafetyTests : IDisposable
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Service : IUpdateServiceControl
    {
        public UpdateServiceState State = UpdateServiceState.Running;
        public bool StopAllowed = true, RestoreAllowed = true, LieAboutRestore;
        public int Stops, Restores;
        public Action? AfterStop;
        public UpdateServiceState ReadState() => State;
        public Task<bool> StopAsync(CancellationToken cancellation)
        {
            Stops++;
            if (!StopAllowed) return Task.FromResult(false);
            State = UpdateServiceState.Stopped; AfterStop?.Invoke();
            return Task.FromResult(true);
        }
        public Task<bool> RestoreAsync(UpdateServiceState original)
        { Restores++; if (RestoreAllowed && !LieAboutRestore) State = original; return Task.FromResult(RestoreAllowed); }
    }
    private readonly Clock _clock = new();
    private readonly Service _service = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RigMD-update-cache-test-" + Guid.NewGuid().ToString("N"));
    public UpdateCacheSafetyTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);
    private ClearWindowsUpdateCacheTool Tool(bool admin = true) => new(NullLoggerFactory.Instance, () => _folder, () => admin, _service, _clock);
    private string FileAt(string name)
    { var path = Path.Combine(_folder, name); File.WriteAllText(path, "disposable test file"); File.SetCreationTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-2)); File.SetLastWriteTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-2)); return path; }
    private static JsonElement Args(string json = "{}") { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private async Task<ClearWindowsUpdateCacheTool> Approved()
    {
        var preview = await Tool().PreviewImpactAsync(Args()); Assert.True(preview.CanExecute, preview.WhatWillHappen);
        var executor = Tool(); Assert.True(executor.TryBindApprovalContext(preview.ApprovalContext!));
        return executor;
    }

    [Fact]
    public async Task CrossRequestApprovalKeepsOriginalFilesAndRestoresRunningService()
    {
        var original = FileAt("original.bin"); var executor = await Approved(); var later = FileAt("later.bin");
        Assert.Equal(1, (await executor.PreviewImpactAsync(Args())).AffectedItemsCount);
        var result = await executor.ExecuteAsync(Args());
        Assert.True(result.Success, result.Summary); Assert.False(File.Exists(original)); Assert.True(File.Exists(later));
        Assert.Equal(UpdateServiceState.Running, _service.State); Assert.Equal(1, _service.Stops); Assert.Equal(1, _service.Restores);
        Assert.False((await executor.ExecuteAsync(Args())).Success);
    }
    [Fact]
    public async Task ServiceThatWasStoppedIsNeverStartedByCleanup()
    {
        _service.State = UpdateServiceState.Stopped; var file = FileAt("old.bin"); var tool = await Approved();
        Assert.True((await tool.ExecuteAsync(Args())).Success); Assert.False(File.Exists(file));
        Assert.Equal(0, _service.Stops); Assert.Equal(UpdateServiceState.Stopped, _service.State);
    }
    [Fact]
    public async Task FailedStopPreventsDeletionAndVerifiesRecovery()
    {
        var file = FileAt("keep.bin"); var tool = await Approved(); _service.StopAllowed = false;
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.True(File.Exists(file));
        Assert.Equal(1, _service.Restores); Assert.Equal(UpdateServiceState.Running, _service.State);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(true, true)]
    public async Task FailedOrFalselyReportedRecoveryCannotBecomeSuccess(bool allowed, bool lie)
    {
        var file = FileAt("old.bin"); var tool = await Approved(); _service.RestoreAllowed = allowed; _service.LieAboutRestore = lie;
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.False(File.Exists(file));
        Assert.Contains("recovery could not be verified", result.Summary); Assert.Equal(UpdateServiceState.Stopped, _service.State);
    }
    [Fact]
    public async Task CancellationAfterServiceStopStillRestoresOriginalState()
    {
        var file = FileAt("keep.bin"); var tool = await Approved(); using var cancellation = new CancellationTokenSource();
        _service.AfterStop = () => cancellation.Cancel();
        var result = await tool.ExecuteAsync(Args(), cancellationToken: cancellation.Token);
        Assert.False(result.Success); Assert.True(File.Exists(file)); Assert.Equal(UpdateServiceState.Running, _service.State); Assert.Equal(1, _service.Restores);
    }
    [Fact]
    public async Task AStopThatThrowsAfterBeingAcceptedStillRestoresWithoutDeleting()
    {
        var file = FileAt("keep.bin"); var tool = await Approved();
        _service.AfterStop = () => throw new IOException("Synthetic loss of stop confirmation");
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.True(File.Exists(file));
        Assert.Equal(UpdateServiceState.Running, _service.State); Assert.Equal(1, _service.Restores);
    }
    [Fact]
    public async Task PartialDeletionIsDisclosedAndNotReportedAsFullSuccess()
    {
        var original = FileAt("unchanged.bin"); var changed = FileAt("changed.bin"); var tool = await Approved();
        File.AppendAllText(changed, "modified since preview");
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.False(File.Exists(original)); Assert.True(File.Exists(changed));
        Assert.Contains("Not all approved files", result.Summary); Assert.Equal(UpdateServiceState.Running, _service.State);
    }
    [Fact]
    public async Task ChangedServiceStateAndExpiredSnapshotFailBeforeServiceMutation()
    {
        var file = FileAt("keep.bin"); var tool = await Approved(); _service.State = UpdateServiceState.Stopped;
        Assert.False((await tool.ExecuteAsync(Args())).Success); Assert.True(File.Exists(file)); Assert.Equal(0, _service.Stops); Assert.Equal(0, _service.Restores);
        _service.State = UpdateServiceState.Running; tool = await Approved(); _clock.Now = _clock.Now.AddMinutes(6);
        Assert.False((await tool.ExecuteAsync(Args())).Success); Assert.Equal(0, _service.Stops); Assert.True(File.Exists(file));
    }
    [Theory]
    [InlineData("[]")] [InlineData("null")] [InlineData("{\"path\":\"C:\\\\Windows\"}")]
    [InlineData("{\"service\":\"other\"}")] [InlineData("{\"x\":1,\"x\":2}")]
    public async Task ArgumentsCannotChangeCleanupOrServiceTargets(string arguments)
    {
        var file = FileAt("keep.bin"); var tool = Tool(); Assert.False((await tool.PreviewImpactAsync(Args(arguments))).CanExecute);
        Assert.False((await tool.ExecuteAsync(Args(arguments))).Success); Assert.True(File.Exists(file)); Assert.Equal(0, _service.Stops);
    }
    [Fact]
    public async Task MissingApprovalAdminAndBusyServiceNeverStartCleanup()
    {
        var file = FileAt("keep.bin"); Assert.False((await Tool().ExecuteAsync(Args())).Success);
        Assert.False((await Tool(false).PreviewImpactAsync(Args())).CanExecute);
        _service.State = UpdateServiceState.Unknown; Assert.False((await Tool().PreviewImpactAsync(Args())).CanExecute);
        Assert.True(File.Exists(file)); Assert.Equal(0, _service.Stops);
        Assert.False((await new ClearWindowsUpdateCacheAction(NullLogger<ClearWindowsUpdateCacheAction>.Instance).ExecuteAsync()).Success);
    }
}
