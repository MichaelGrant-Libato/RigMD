using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Tests.Infrastructure;

public sealed class TempCleanupSnapshotTests : IDisposable
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private readonly Clock _clock = new();
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "RigMD-temp-snapshot-" + Guid.NewGuid().ToString("N"));
    private string User => Path.Combine(_fixture, "user");
    private string Windows => Path.Combine(_fixture, "windows");
    private string Outside => Path.Combine(_fixture, "outside");
    public TempCleanupSnapshotTests() { Directory.CreateDirectory(User); Directory.CreateDirectory(Windows); Directory.CreateDirectory(Outside); }
    public void Dispose() { Directory.Delete(_fixture, true); }
    private ClearTempFilesTool Tool(bool admin = true, Func<string>? user = null, Func<string>? windows = null) => new(NullLoggerFactory.Instance, user ?? (() => User), windows ?? (() => Windows), _clock, () => admin);
    private static JsonElement Args(string value = "{}") { using var json = JsonDocument.Parse(value); return json.RootElement.Clone(); }
    private string FileAt(string root, string name, double createdMinutes = 120, double? writtenMinutes = null)
    {
        var path = Path.Combine(root, name); File.WriteAllText(path, "disposable fixture");
        File.SetCreationTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-createdMinutes));
        File.SetLastWriteTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-(writtenMinutes ?? createdMinutes)));
        return path;
    }
    private static void Approve(ClearTempFilesTool tool, ToolDryRunPreview preview)
    { Assert.True(preview.CanExecute, preview.WhatWillHappen); Assert.NotNull(preview.ApprovalContext); Assert.True(tool.TryBindApprovalContext(preview.ApprovalContext)); }

    [Fact]
    public async Task SnapshotSurvivesRequestScopeAndDeletesOnlyPreviewedFiles()
    {
        var original = FileAt(User, "original.tmp"); var outside = FileAt(Outside, "keep.txt");
        var preview = await Tool().PreviewImpactAsync(Args()); Assert.Equal(1, preview.AffectedItemsCount);
        var newFile = FileAt(User, "created-after-preview.tmp");
        var secondRequestTool = Tool(); Approve(secondRequestTool, preview);
        // Orchestrator re-previews must reuse the consumed approval's snapshot, not expand it.
        Assert.Equal(1, (await secondRequestTool.PreviewImpactAsync(Args())).AffectedItemsCount);
        var result = await secondRequestTool.ExecuteAsync(Args());
        Assert.True(result.Success, result.Summary); Assert.False(File.Exists(original)); Assert.True(File.Exists(newFile)); Assert.True(File.Exists(outside));
        Assert.False((await secondRequestTool.ExecuteAsync(Args())).Success);
        Assert.False(Tool().TryBindApprovalContext(preview.ApprovalContext!));
    }
    [Fact]
    public async Task ExecutionWithoutApprovalAndLegacyActionCannotDelete()
    {
        var file = FileAt(User, "keep.tmp"); var tool = Tool(); await tool.PreviewImpactAsync(Args());
        Assert.False((await tool.ExecuteAsync(Args())).Success);
        Assert.False((await new ClearTempFilesAction(NullLogger<ClearTempFilesAction>.Instance).ExecuteAsync(User)).Success);
        Assert.True(File.Exists(file));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task IncludeWindowsTempAndAgeAreSharedByPreviewAndExecution(bool include)
    {
        var old = FileAt(User, "old.tmp", 61); var boundary = FileAt(User, "boundary.tmp", 60);
        var young = FileAt(User, "young.tmp", 59); var copied = FileAt(User, "copied.tmp", 1, 120);
        var future = FileAt(User, "future.tmp", -1); var win = FileAt(Windows, "old.tmp", 120);
        var args = Args($"{{\"includeWindowsTemp\":{include.ToString().ToLowerInvariant()},\"minAgeMinutes\":60}}");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(args); Assert.Equal(include ? 3 : 2, preview.AffectedItemsCount);
        Approve(tool, preview); var result = await tool.ExecuteAsync(args); Assert.True(result.Success, result.Summary);
        Assert.False(File.Exists(old)); Assert.False(File.Exists(boundary)); Assert.True(File.Exists(young)); Assert.True(File.Exists(copied)); Assert.True(File.Exists(future)); Assert.Equal(!include, File.Exists(win));
    }
    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("{\"minAgeMinutes\":null}")] [InlineData("{\"minAgeMinutes\":-1}")]
    [InlineData("{\"minAgeMinutes\":525601}")] [InlineData("{\"minAgeMinutes\":1.5}")] [InlineData("{\"minAgeMinutes\":\"60\"}")]
    [InlineData("{\"includeWindowsTemp\":1}")] [InlineData("{\"unexpected\":true}")] [InlineData("{\"minAgeMinutes\":1,\"minAgeMinutes\":2}")]
    public async Task InvalidArgumentsCannotObtainAnExecutablePreview(string json)
    {
        var file = FileAt(User, "keep.tmp"); var tool = Tool(); var args = Args(json);
        Assert.False((await tool.PreviewImpactAsync(args)).CanExecute); Assert.False((await tool.ExecuteAsync(args)).Success); Assert.True(File.Exists(file));
    }
    [Fact]
    public async Task ReplacingAFileAtTheSamePathSkipsItAndKeepsUnchangedApprovedCandidates()
    {
        var original = FileAt(User, "changed.tmp"); var other = FileAt(User, "other.tmp");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        File.Move(original, Path.Combine(Outside, "moved.tmp")); FileAt(User, "changed.tmp");
        Approve(tool, preview); var result = await tool.ExecuteAsync(Args()); Assert.True(result.Success, result.Summary);
        Assert.True(File.Exists(original)); Assert.False(File.Exists(other));
        Assert.Contains("1 changed or missing", result.Summary);
        Assert.Equal("1", result.Proof.Single(item => item.Label == "Temporary files deleted").After);
    }
    [Fact]
    public async Task ModifyingAFileAfterPreviewRequiresNewReview()
    {
        var file = FileAt(User, "changed.tmp"); var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        File.AppendAllText(file, "changed"); Approve(tool, preview);
        var result = await tool.ExecuteAsync(Args()); Assert.False(result.Success); Assert.True(File.Exists(file));
        Assert.Contains("No files were removed", result.Summary);
        Assert.Equal("0", result.Proof.Single(item => item.Label == "Temporary files deleted").After);
        Assert.Equal("1", result.Proof.Single(item => item.Label == "Previewed files not removed").After);
    }
    [Fact]
    public async Task MissingApprovedFileDoesNotPreventDeletingUnchangedApprovedFilesOrExpandTheSnapshot()
    {
        var gone = FileAt(User, "gone.tmp"); var unchanged = FileAt(User, "unchanged.tmp");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        File.Delete(gone); var added = FileAt(User, "new.tmp");
        Approve(tool, preview); var result = await tool.ExecuteAsync(Args());
        Assert.True(result.Success, result.Summary); Assert.False(File.Exists(unchanged)); Assert.True(File.Exists(added));
        Assert.Equal("2", result.Proof.Single(item => item.Label == "Files included in preview").After);
        Assert.Equal("1", result.Proof.Single(item => item.Label == "Temporary files deleted").After);
        Assert.Contains("1 changed or missing", result.Summary);
    }
    [Fact]
    public async Task ReplacingTheCleanupRootStopsDeletionAndDoesNotTrustItsNewContents()
    {
        FileAt(User, "original.tmp"); var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        var moved = Path.Combine(_fixture, "old-user"); Directory.Move(User, moved); Directory.CreateDirectory(User);
        var replacement = FileAt(User, "original.tmp");
        Approve(tool, preview); var result = await tool.ExecuteAsync(Args());
        Assert.False(result.Success); Assert.True(File.Exists(replacement)); Assert.True(File.Exists(Path.Combine(moved, "original.tmp")));
        Assert.Equal("0", result.Proof.Single(item => item.Label == "Temporary files deleted").After);
    }
    [Fact]
    public async Task AlreadyCanceledAndMidCleanupCancellationAreHonored()
    {
        var a = FileAt(User, "a.tmp"); FileAt(User, "b.tmp");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args()); Approve(tool, preview);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.ExecuteAsync(Args(), cancellationToken: new CancellationToken(true)));
        Assert.True(File.Exists(a));
        tool = Tool(); preview = await tool.PreviewImpactAsync(Args()); Approve(tool, preview);
        using var cancellation = new CancellationTokenSource();
        var result = await tool.ExecuteAsync(Args(), _ => cancellation.Cancel(), cancellation.Token);
        Assert.False(result.Success); Assert.Contains("canceled", result.Summary, StringComparison.OrdinalIgnoreCase); Assert.Single(Directory.GetFiles(User));
    }
    [Fact]
    public async Task LockedFilesAreSkippedWhileUnchangedApprovedFilesCanBeDeleted()
    {
        var locked = FileAt(User, "locked.tmp"); var free = FileAt(User, "free.tmp");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args()); Approve(tool, preview);
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { var result = await tool.ExecuteAsync(Args()); Assert.True(result.Success, result.Summary); }
        Assert.True(File.Exists(locked)); Assert.False(File.Exists(free));
    }
    [Fact]
    public async Task ExpiryChangedParametersChangedRootsAndDeniedAdminFailClosed()
    {
        var file = FileAt(User, "keep.tmp"); var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        _clock.Now = _clock.Now.AddMinutes(5); Assert.False(tool.TryBindApprovalContext(preview.ApprovalContext!));
        preview = await tool.PreviewImpactAsync(Args()); Approve(tool, preview);
        Assert.False((await tool.ExecuteAsync(Args("{\"includeWindowsTemp\":true}"))).Success);
        tool = Tool(); preview = await tool.PreviewImpactAsync(Args());
        var changedRoot = Tool(user: () => Outside); Approve(changedRoot, preview); Assert.False((await changedRoot.ExecuteAsync(Args())).Success);
        Assert.False((await Tool(false).PreviewImpactAsync(Args("{\"includeWindowsTemp\":true}"))).CanExecute);
        Assert.True(File.Exists(file));
    }
    [Fact]
    public async Task ReversibleFlagIsFalseAndPrivateSnapshotIsNotSerialized()
    {
        FileAt(User, "a.tmp"); var preview = await Tool().PreviewImpactAsync(Args());
        Assert.Equal(ToolSafetyTier.Tier2_DestructiveOrAdmin, preview.SafetyTier); Assert.True(preview.RequiresUserConfirmation);
        var json = JsonSerializer.Serialize(preview); Assert.DoesNotContain("ApprovalContext", json); Assert.DoesNotContain("a.tmp", json);
    }
    [Fact]
    public async Task RootAndAncestorJunctionsAreRejectedAndChildJunctionsAreSkipped()
    {
        var file = FileAt(Outside, "keep.tmp"); var link = Path.Combine(_fixture, "link");
        await CreateJunction(link, Outside);
        try
        {
            Assert.False((await Tool(user: () => link).PreviewImpactAsync(Args())).CanExecute);
            Directory.CreateDirectory(Path.Combine(Outside, "nested"));
            Assert.False((await Tool(user: () => Path.Combine(link, "nested")).PreviewImpactAsync(Args())).CanExecute);
            var child = Path.Combine(User, "link");
            await CreateJunction(child, Outside);
            try { var preview = await Tool().PreviewImpactAsync(Args()); Assert.True(preview.CanExecute); Assert.Equal(0, preview.AffectedItemsCount); Assert.True(File.Exists(file)); }
            finally { Directory.Delete(child); }
        }
        finally { Directory.Delete(link); }
    }

    private async Task CreateJunction(string link, string target)
    {
        // Only disposable paths under this fixture are passed to cmd's built-in mklink.
        Assert.StartsWith(_fixture + Path.DirectorySeparatorChar, link, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_fixture + Path.DirectorySeparatorChar, target, StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = $"/d /v:off /c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
