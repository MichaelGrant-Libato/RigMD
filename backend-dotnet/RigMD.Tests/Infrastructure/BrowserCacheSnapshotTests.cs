using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;
using RigMD.Application.Models;
using RigMD.Infrastructure.Remediation.Actions;
using RigMD.Infrastructure.Remediation.Tools.Remediation;

namespace RigMD.Tests.Infrastructure;

public sealed class BrowserCacheSnapshotTests : IDisposable
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private readonly Clock _clock = new();
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "RigMD-browser-snapshot-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(_fixture, "account");
    private string Outside => Path.Combine(_fixture, "outside");
    private string Chrome => Path.Combine(Local, "Google", "Chrome", "User Data", "Default", "Cache");
    private string Edge => Path.Combine(Local, "Microsoft", "Edge", "User Data", "Default", "Cache");
    private string Firefox(string profile = "one.default") => Path.Combine(Local, "Mozilla", "Firefox", "Profiles", profile, "cache2");
    public BrowserCacheSnapshotTests() { Directory.CreateDirectory(Local); Directory.CreateDirectory(Outside); }
    public void Dispose() => Directory.Delete(_fixture, true);
    private ClearBrowserCacheTool Tool(Func<string>? account = null) => new(NullLoggerFactory.Instance, account ?? (() => Local), _clock);
    private static JsonElement Args(string json = "{}") { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private static JsonElement Only(string browser) => Args(JsonSerializer.Serialize(new { browsers = new[] { browser } }));
    private string FileAt(string folder, string name = "entry.bin")
    {
        Directory.CreateDirectory(folder); var path = Path.Combine(folder, name);
        File.WriteAllText(path, "disposable cache fixture");
        File.SetCreationTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-2));
        File.SetLastWriteTimeUtc(path, _clock.Now.UtcDateTime.AddMinutes(-2));
        return path;
    }
    private static void Approve(ClearBrowserCacheTool tool, ToolDryRunPreview preview)
    {
        Assert.True(preview.CanExecute, preview.WhatWillHappen);
        Assert.NotNull(preview.ApprovalContext);
        Assert.True(tool.TryBindApprovalContext(preview.ApprovalContext));
    }

    [Theory]
    [InlineData("chrome")] [InlineData("edge")] [InlineData("firefox")]
    public async Task OnlyTheApprovedBrowserCacheIsDeletedAcrossRequestScopes(string browser)
    {
        var caches = new Dictionary<string, string> { ["chrome"] = FileAt(Chrome), ["edge"] = FileAt(Edge), ["firefox"] = FileAt(Firefox()) };
        var history = FileAt(Path.GetDirectoryName(Chrome)!, "History");
        var cookies = FileAt(Path.GetDirectoryName(Firefox())!, "cookies.sqlite");
        var outside = FileAt(Outside);
        var args = Only(browser);
        var preview = await Tool().PreviewImpactAsync(args);
        Assert.Equal(1, preview.AffectedItemsCount); Assert.Single(preview.AffectedTargets);
        Assert.Equal(new FileInfo(caches[browser]).Length, preview.EstimatedBytesAffected);
        Assert.All(caches.Values, file => Assert.True(File.Exists(file)));
        var added = FileAt(Path.GetDirectoryName(caches[browser])!, "added-after-preview.bin");
        var store = new RemediationApprovalStore(_clock);
        var scope = RemediationApprovalStore.CreateScope("test", Guid.NewGuid(), "clear_browser_cache", args.GetRawText());
        var approval = store.Issue(scope, preview.ApprovalContext);
        Assert.True(store.TryConsume(approval.Token, scope, out var context));
        var executor = Tool(); Assert.True(executor.TryBindApprovalContext(context!));
        Assert.Equal(1, (await executor.PreviewImpactAsync(args)).AffectedItemsCount);
        var result = await executor.ExecuteAsync(args);
        Assert.True(result.Success, result.Summary);
        foreach (var (name, file) in caches) Assert.Equal(name != browser, File.Exists(file));
        Assert.True(File.Exists(history)); Assert.True(File.Exists(cookies)); Assert.True(File.Exists(added)); Assert.True(File.Exists(outside));
        Assert.Equal("1", result.Proof.Single(p => p.Label == "Browser cache files deleted").After);
        Assert.Contains("not guaranteed recovered drive space", result.Proof.Single(p => p.Label == "Size of deleted files").Meaning);
        Assert.False((await executor.ExecuteAsync(args)).Success);
        Assert.False(Tool().TryBindApprovalContext(preview.ApprovalContext!));
        Assert.False(store.TryConsume(approval.Token, scope));
    }

    [Fact]
    public async Task OmittedSelectionIncludesSupportedCachesAndLeavesOtherProfilesAlone()
    {
        var selected = new[] { FileAt(Chrome), FileAt(Edge), FileAt(Firefox()), FileAt(Firefox("two.default")) };
        var other = FileAt(Path.Combine(Local, "Google", "Chrome", "User Data", "Profile 1", "Cache"));
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args());
        Assert.Equal(4, preview.AffectedItemsCount); Assert.Equal(3, preview.AffectedTargets.Count);
        var newProfile = FileAt(Firefox("created-after-preview"));
        Approve(tool, preview); Assert.True((await tool.ExecuteAsync(Args())).Success);
        Assert.All(selected, path => Assert.False(File.Exists(path)));
        Assert.True(File.Exists(other)); Assert.True(File.Exists(newProfile));
    }

    [Fact]
    public async Task SelectionOrderAndCaseDoNotChangeTheApprovedBrowserSet()
    {
        var chrome = FileAt(Chrome); var edge = FileAt(Edge); var firefox = FileAt(Firefox());
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args("{\"browsers\":[\"EDGE\",\"Chrome\"]}"));
        Assert.Equal(2, preview.AffectedItemsCount); Approve(tool, preview);
        Assert.True((await tool.ExecuteAsync(Args("{\"browsers\":[\"chrome\",\"edge\"]}"))).Success);
        Assert.False(File.Exists(chrome)); Assert.False(File.Exists(edge)); Assert.True(File.Exists(firefox));
    }

    [Fact]
    public async Task BoundedSnapshotsDiscloseThatFurtherCleanupNeedsAnotherReview()
    {
        for (var i = 0; i < 501; i++) FileAt(Chrome, i.ToString());
        var preview = await Tool().PreviewImpactAsync(Only("chrome"));
        Assert.InRange(preview.AffectedItemsCount, 1, 500);
        Assert.Contains(preview.Warnings, warning => warning.Contains("partial", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(501, Directory.GetFiles(Chrome).Length);
    }

    [Theory]
    [InlineData("null")] [InlineData("[]")] [InlineData("{\"browsers\":null}")] [InlineData("{\"browsers\":\"chrome\"}")]
    [InlineData("{\"browsers\":[]}")] [InlineData("{\"browsers\":[\"chrome-other\"]}")] [InlineData("{\"browsers\":[\"chrome\",\"Chrome\"]}")]
    [InlineData("{\"browsers\":[\"safari\"]}")] [InlineData("{\"browsers\":[null]}")] [InlineData("{\"browsers\":[1]}")]
    [InlineData("{\"browsers\":[\" chrome\"]}")] [InlineData("{\"unknown\":true}")]
    [InlineData("{\"browsers\":[\"chrome\"],\"browsers\":[\"edge\"]}")]
    public async Task InvalidSelectionsCannotPreviewOrExecute(string json)
    {
        var file = FileAt(Chrome); var tool = Tool();
        Assert.False((await tool.PreviewImpactAsync(Args(json))).CanExecute);
        Assert.False((await tool.ExecuteAsync(Args(json))).Success); Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task PreviewAloneAndLegacyDispatchCannotAuthorizeDeletion()
    {
        var file = FileAt(Chrome); var tool = Tool();
        Assert.True((await tool.PreviewImpactAsync(Only("chrome"))).CanExecute);
        Assert.False((await tool.ExecuteAsync(Only("chrome"))).Success);
        Assert.False((await new ClearBrowserCacheAction(NullLogger<ClearBrowserCacheAction>.Instance).ExecuteAsync()).Success);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task ChangedBrowserSelectionAccountAndExpiredApprovalsFailClosed()
    {
        var chrome = FileAt(Chrome); var edge = FileAt(Edge);
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Only("chrome")); Approve(tool, preview);
        Assert.False((await tool.ExecuteAsync(Only("edge"))).Success);
        preview = await Tool().PreviewImpactAsync(Only("chrome"));
        var otherAccount = Path.Combine(_fixture, "other-account"); Directory.CreateDirectory(otherAccount);
        var other = Tool(() => otherAccount); Approve(other, preview);
        Assert.False((await other.PreviewImpactAsync(Only("chrome"))).CanExecute);
        Assert.False((await other.ExecuteAsync(Only("chrome"))).Success);
        tool = Tool(); preview = await tool.PreviewImpactAsync(Only("chrome")); Approve(tool, preview);
        _clock.Now = _clock.Now.AddMinutes(5);
        Assert.False((await tool.ExecuteAsync(Only("chrome"))).Success);
        Assert.False(Tool().TryBindApprovalContext(preview.ApprovalContext!));
        Assert.True(File.Exists(chrome)); Assert.True(File.Exists(edge));
    }

    [Fact]
    public async Task MissingChangedAndLockedFilesAreSkippedWithoutExpandingTheSnapshot()
    {
        var missing = FileAt(Chrome, "missing"); var changed = FileAt(Chrome, "changed");
        var locked = FileAt(Chrome, "locked"); var unchanged = FileAt(Chrome, "unchanged");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Only("chrome")); Approve(tool, preview);
        File.Delete(missing); File.AppendAllText(changed, "new content");
        var added = FileAt(Chrome, "new");
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = await tool.ExecuteAsync(Only("chrome"));
            Assert.True(result.Success, result.Summary);
            Assert.Equal("1", result.Proof.Single(p => p.Label == "Browser cache files deleted").After);
            Assert.Equal("3", result.Proof.Single(p => p.Label == "Previewed files not removed").After);
        }
        Assert.False(File.Exists(unchanged)); Assert.True(File.Exists(changed)); Assert.True(File.Exists(locked)); Assert.True(File.Exists(added));
    }

    [Fact]
    public async Task RootReplacementStopsAllApprovedBrowsersBeforeAnyDeletion()
    {
        var original = FileAt(Chrome); var edge = FileAt(Edge);
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Args()); Approve(tool, preview);
        var old = Path.Combine(Outside, "old-cache"); Directory.Move(Chrome, old);
        var replacement = FileAt(Chrome);
        var result = await tool.ExecuteAsync(Args());
        Assert.False(result.Success); Assert.True(File.Exists(replacement)); Assert.True(File.Exists(edge));
        Assert.True(File.Exists(Path.Combine(old, Path.GetFileName(original))));
    }

    [Fact]
    public async Task CancellationReportsPartialDeletionWithoutReplayingTheApproval()
    {
        FileAt(Chrome, "one"); FileAt(Chrome, "two");
        var tool = Tool(); var preview = await tool.PreviewImpactAsync(Only("chrome")); Approve(tool, preview);
        using var cancellation = new CancellationTokenSource();
        var result = await tool.ExecuteAsync(Only("chrome"), _ => cancellation.Cancel(), cancellation.Token);
        Assert.False(result.Success); Assert.Contains("canceled", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.GetFiles(Chrome)); Assert.False((await tool.ExecuteAsync(Only("chrome"))).Success);
    }

    [Fact]
    public async Task EmptyCachesHaveNoExecutablePreviewAndPrivateSnapshotsNeverEnterJson()
    {
        var tool = Tool(); Assert.False((await tool.PreviewImpactAsync(Args())).CanExecute);
        FileAt(Chrome, "private-cache-entry");
        var preview = await tool.PreviewImpactAsync(Only("chrome"));
        Assert.Equal(ToolSafetyTier.Tier2_DestructiveOrAdmin, preview.SafetyTier); Assert.True(preview.RequiresUserConfirmation);
        var json = JsonSerializer.Serialize(preview);
        Assert.DoesNotContain("ApprovalContext", json); Assert.DoesNotContain("private-cache-entry", json);
        Assert.DoesNotContain(_fixture, json);
        Assert.False(tool.TryBindApprovalContext(new object()));
        var temp = new ClearTempFilesTool(NullLoggerFactory.Instance, () => Chrome, () => Outside, _clock, () => true);
        var tempPreview = await temp.PreviewImpactAsync(Args());
        Assert.False(tool.TryBindApprovalContext(tempPreview.ApprovalContext!));
        Assert.False(temp.TryBindApprovalContext(preview.ApprovalContext!));
    }

    [Fact]
    public async Task BrowserJunctionsAreRejectedOrSkippedWithoutTouchingOutsideFiles()
    {
        var outside = FileAt(Outside);
        Directory.CreateDirectory(Path.GetDirectoryName(Chrome)!);
        await Junction(Chrome, Outside);
        try { Assert.False((await Tool().PreviewImpactAsync(Only("chrome"))).CanExecute); }
        finally { Directory.Delete(Chrome); }
        var original = FileAt(Chrome);
        var child = Path.Combine(Chrome, "linked-cache"); await Junction(child, Outside);
        try
        {
            var tool = Tool(); var preview = await tool.PreviewImpactAsync(Only("chrome"));
            Assert.Equal(1, preview.AffectedItemsCount); Approve(tool, preview);
            Assert.True((await tool.ExecuteAsync(Only("chrome"))).Success);
            Assert.False(File.Exists(original)); Assert.True(File.Exists(outside));
        }
        finally { Directory.Delete(child); }
        var profiles = Path.GetDirectoryName(Path.GetDirectoryName(Firefox())!)!;
        Directory.CreateDirectory(Path.GetDirectoryName(profiles)!);
        await Junction(profiles, Outside);
        try { Assert.False((await Tool().PreviewImpactAsync(Only("firefox"))).CanExecute); }
        finally { Directory.Delete(profiles); }
        Assert.True(File.Exists(outside));
    }

    private async Task Junction(string link, string target)
    {
        Assert.StartsWith(_fixture + Path.DirectorySeparatorChar, Path.GetFullPath(link), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_fixture + Path.DirectorySeparatorChar, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
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
