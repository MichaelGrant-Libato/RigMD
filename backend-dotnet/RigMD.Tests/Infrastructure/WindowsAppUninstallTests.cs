using Microsoft.Win32;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class WindowsAppUninstallTests
{
    private const string AppId = "registry/LocalMachine/Registry64/{8E574825-151B-4983-904C-C6B1253E7BDF}";

    [Theory]
    [InlineData(AppId, true)]
    [InlineData("registry/CurrentUser/Registry64/{8E574825-151B-4983-904C-C6B1253E7BDF}", false)]
    [InlineData("registry/LocalMachine/Default/{8E574825-151B-4983-904C-C6B1253E7BDF}", false)]
    [InlineData("registry/LocalMachine/Registry64/Example App", false)]
    [InlineData("registry/LocalMachine/Registry64/{8E574825-151B-4983-904C-C6B1253E7BDF}/other", false)]
    public void AcceptsOnlyExactMachineMsiRegistrationIds(string appId, bool valid)
    {
        Assert.Equal(valid, MachineMsiRegistration.TryParse(appId, out var view, out var code));
        if (valid)
        {
            Assert.Equal(RegistryView.Registry64, view);
            Assert.Equal("{8E574825-151B-4983-904C-C6B1253E7BDF}", MachineMsiRegistration.FormatCode(code));
        }
    }

    [Fact]
    public void WinGetTargetsOnlyTheVerifiedProductCodeAndMachineScope()
    {
        var code = Guid.Parse("{8E574825-151B-4983-904C-C6B1253E7BDF}");
        Assert.Equal(new[] { "uninstall", "--product-code", "{8E574825-151B-4983-904C-C6B1253E7BDF}",
            "--scope", "machine", "--exact", "--interactive", "--disable-interactivity", "--source", "winget" },
            WindowsAppUninstallPlatform.BuildArguments(code));
    }

    [Fact]
    public async Task ReviewIsReadOnlyAndApprovalCanLaunchExactlyOnce()
    {
        var platform = new Platform { Gone = true };
        var service = new WindowsAppUninstallService(TimeProvider.System, platform);
        var review = await service.ReviewAsync(AppId, default);
        Assert.Equal("Example", review.Name);
        Assert.Equal("1.2.3", review.Version);
        Assert.Empty(platform.Invocations);
        Assert.Equal("unavailable", (await service.UninstallAsync(Guid.NewGuid(), default)).Status);

        var result = await service.UninstallAsync(review.ApprovalId!.Value, default);
        Assert.Equal("removed", result.Status);
        Assert.Equal(platform.Target!.ProductCode, Assert.Single(platform.Invocations));
        Assert.Equal("unavailable", (await service.UninstallAsync(review.ApprovalId.Value, default)).Status);
        Assert.Single(platform.Invocations);
    }

    [Fact]
    public async Task ExpiredChangedAndUnsupportedTargetsDoNotLaunch()
    {
        var platform = new Platform();
        var clock = new TestClock();
        var service = new WindowsAppUninstallService(clock, platform);
        var review = await service.ReviewAsync(AppId, default);
        clock.Now = clock.Now.AddMinutes(3);
        Assert.Equal("unavailable", (await service.UninstallAsync(review.ApprovalId!.Value, default)).Status);

        review = await service.ReviewAsync(AppId, default);
        platform.Target = platform.Target! with { Version = "2.0" };
        Assert.Equal("unavailable", (await service.UninstallAsync(review.ApprovalId!.Value, default)).Status);

        platform.WinGetPath = null;
        Assert.Null((await service.ReviewAsync(AppId, default)).ApprovalId);
        platform.Target = null;
        Assert.Null((await service.ReviewAsync(AppId, default)).ApprovalId);
        Assert.Empty(platform.Invocations);
    }

    [Fact]
    public async Task InstallerRunIsExclusiveAndExitZeroDoesNotProveRemoval()
    {
        var pending = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var platform = new Platform { Process = pending.Task };
        var service = new WindowsAppUninstallService(TimeProvider.System, platform);
        var first = await service.ReviewAsync(AppId, default);
        var second = await service.ReviewAsync(AppId, default);
        var launched = service.UninstallAsync(first.ApprovalId!.Value, default);
        Assert.Equal("busy", (await service.UninstallAsync(second.ApprovalId!.Value, default)).Status);
        pending.SetResult(0);
        Assert.Equal("unverified", (await launched).Status);
        Assert.Single(platform.Invocations);
    }

    [Theory]
    [InlineData(1602, "cancelled")]
    [InlineData(3010, "restart-required")]
    [InlineData(unchecked((int)0x8A150016), "unavailable")]
    [InlineData(12345, "failed")]
    public void DistinguishesReportedWinGetOutcomes(int code, string expected) =>
        Assert.Equal(expected, WindowsAppUninstallService.InterpretResult(code, false).Status);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Platform : IWindowsAppUninstallPlatform
    {
        internal MachineMsiApp? Target = new(AppId, Guid.Parse("{8E574825-151B-4983-904C-C6B1253E7BDF}"),
            "Example", "1.2.3", "1.2.3");
        internal string? WinGetPath = @"C:\Program Files\WindowsApps\fixture\winget.exe";
        internal bool Gone;
        internal Task<int> Process = Task.FromResult(0);
        internal List<Guid> Invocations { get; } = new();
        public MachineMsiApp? Inspect(string appId) => Target?.AppId == appId ? Target : null;
        public Task<string?> FindWinGetAsync(CancellationToken cancellationToken) => Task.FromResult(WinGetPath);
        public Task<int> RunUninstallAsync(string path, Guid productCode)
        {
            Assert.Equal(WinGetPath, path);
            Invocations.Add(productCode);
            return Process;
        }
        public bool IsGone(Guid productCode) => Gone && Target?.ProductCode == productCode;
    }
}
