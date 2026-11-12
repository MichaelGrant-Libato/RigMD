using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public class AppUninstallServiceTests
{
    private static readonly Guid SessionId = Guid.Parse("d4627bca-0c31-4026-a385-567ce6fe3a01");
    private const string Metric = "storage_usage_C:";
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Platform : IAppUninstallPlatform
    {
        public StorageAppEntry App { get; set; } = new("Example Editor", "Example Company", "1.0", 1024, "Machine registration")
        { Id = new string('A', 64), CanUninstall = true, ProductCode = "{D4627BCA-0C31-4026-A385-567CE6FE3A01}" };
        public bool IsAdministrator { get; set; } = true;
        public bool Installed { get; set; } = true;
        public long Free { get; set; } = 10;
        public int Calls { get; private set; }
        public bool LoseTracking { get; set; }
        public TaskCompletionSource Launched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public StorageAppSnapshot Capture() => new(DateTimeOffset.UtcNow, [App], false, "Test inventory");
        public InstallerEvidence Inspect(string productCode) => Installed ? new("installed", App.Name, App.Publisher, App.Version) : new("absent");
        public InstallerEvidence Inspect(StorageAppEntry app) => Inspect(app.ProductCode ?? app.Id);
        public Task<int> UninstallAsync(StorageAppEntry app) => UninstallAsync(app.ProductCode ?? app.Id);
        public StorageVolumeReading[] ReadVolumes() => [new("C:\\", Free, 100)];
        public async Task<int> UninstallAsync(string productCode)
        {
            Assert.Equal(App.ProductCode ?? App.Id, productCode);
            Calls++;
            Launched.TrySetResult();
            var code = await Exit.Task;
            if (LoseTracking) throw new UninstallTrackingException(new IOException("Simulated tracking failure"));
            if (code == 0) Installed = false;
            return code;
        }
    }
    private static AppUninstallService Service(Platform platform, Clock? clock = null, LocalRemediationExecutionGate? gate = null)
    {
        clock ??= new();
        return new(platform, new RemediationApprovalStore(clock), gate ?? new(), clock, NullLogger<AppUninstallService>.Instance);
    }

    [Fact]
    public void PreviewAndConsentAreBothRequiredBeforeAnyUninstall()
    {
        var platform = new Platform(); var service = Service(platform);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, null, true));
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        Assert.True(preview.CanExecute);
        Assert.Equal("installed", preview.Before.InstallerStatus);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, false));
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public void PermissionAndCurrentStorageWarningAreRechecked()
    {
        var platform = new Platform(); var service = Service(platform);
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        platform.IsAdministrator = false;
        Assert.False(service.Preview(SessionId, Metric, platform.App.Id).CanExecute);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        platform.IsAdministrator = true; platform.Free = 60;
        Assert.False(service.Preview(SessionId, Metric, platform.App.Id).CanExecute);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public void ChangedAppFindingAndExpiredApprovalCannotBeExecuted()
    {
        var platform = new Platform(); var clock = new Clock(); var service = Service(platform, clock);
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        platform.App = platform.App with { Version = "2.0" };
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        preview = service.Preview(SessionId, Metric, platform.App.Id);
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        preview = service.Preview(SessionId, Metric, platform.App.Id);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, "storage_usage", platform.App.Id, preview.ApprovalToken, true));
        preview = service.Preview(SessionId, Metric, platform.App.Id);
        Assert.Throws<InvalidOperationException>(() => service.Start(Guid.NewGuid(), Metric, platform.App.Id, preview.ApprovalToken, true));
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public async Task RunningUninstallHoldsExecutionGateAndProducesMeasuredBeforeAndAfter()
    {
        var platform = new Platform(); var gate = new LocalRemediationExecutionGate(); var service = Service(platform, gate: gate);
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        var operation = service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true);
        await platform.Launched.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(gate.TryEnter());
        Assert.Equal("running", service.Find(operation.Id)!.Status);
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        platform.Free = 25;
        platform.Exit.SetResult(0);
        for (var attempt = 0; attempt < 600 && service.Find(operation.Id)!.Status == "running"; attempt++) await Task.Delay(5);
        var completed = service.Find(operation.Id)!;
        Assert.Equal("removed", completed.Status);
        Assert.Equal("installed", completed.Before.InstallerStatus);
        Assert.Equal("absent", completed.After!.InstallerStatus);
        Assert.Equal(10, completed.Before.Volumes[0].FreeBytes);
        Assert.Equal(25, completed.After.Volumes[0].FreeBytes);
        Assert.Equal(1, platform.Calls);
        Assert.Single(service.Recent(SessionId));
        Assert.Empty(service.Recent(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
    }

    [Theory]
    [InlineData(0, "absent", "removed")]
    [InlineData(0, "installed", "unverified")]
    [InlineData(0, "unknown", "unverified")]
    [InlineData(1602, "installed", "canceled")]
    [InlineData(1603, "installed", "failed")]
    [InlineData(1603, "absent", "unverified")]
    [InlineData(3010, "absent", "reboot_required")]
    public void AnExitCodeAloneCannotConfirmRemoval(int code, string installerStatus, string expected)
    {
        var snapshot = new AppUninstallSnapshot(DateTimeOffset.UtcNow, "installed", []);
        var operation = new AppUninstallOperation("operation", SessionId, Metric, new Platform().App, "running", "", snapshot);
        Assert.Equal(expected, AppUninstallService.Assess(operation, code, snapshot with { InstallerStatus = installerStatus }).Status);
    }

    [Theory]
    [InlineData(false, 1, null)]
    [InlineData(true, null, null)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 1, "0")]
    public void UntrustedOrNonRemovableRegistrationsHaveNoExecutableUninstall(bool machine, object? installer, object? noRemove)
    {
        var app = new Platform().App;
        var result = StorageAppInventory.Identify(app, "identity", machine, app.ProductCode!, installer, noRemove);
        Assert.False(result.CanUninstall);
        Assert.Null(result.ProductCode);
    }

    [Theory]
    [InlineData("Windows Driver", "Example")]
    [InlineData("Microsoft Defender", "Microsoft Corporation")]
    [InlineData("RigMD Desktop", "Example")]
    public void ProtectedSoftwareIsBlocked(string name, string publisher)
    {
        var platform = new Platform(); platform.App = platform.App with { Name = name, Publisher = publisher };
        Assert.False(Service(platform).Preview(SessionId, Metric, platform.App.Id).CanExecute);
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public void OrdinaryMicrosoftPublisherDoesNotByItselfBlockVerifiedRemoval()
    {
        var platform = new Platform(); platform.App = platform.App with { Publisher = "Microsoft Corporation" };
        Assert.True(Service(platform).Preview(SessionId, Metric, platform.App.Id).CanExecute);
        Assert.Equal(0, platform.Calls);
    }

    private static RegisteredUninstallTarget Executable => new(@"C:\Programs\uninstall.exe", "[\"--uninstall\"]", new string('A', 64), "Example Company", "S-1-5-21-1001");

    [Fact]
    public void ExecutableBytesArgumentsAndAccountAreBoundToSingleUseApproval()
    {
        var platform = new Platform();
        platform.App = platform.App with { ProductCode = null, RegisteredTarget = Executable, UninstallMethod = "registered_exe" };
        var service = Service(platform);
        foreach (var changed in new[] { Executable with { Sha256 = new string('B', 64) }, Executable with { ArgumentsJson = "[\"--remove\"]" },
            Executable with { UserSid = "S-1-5-21-1002" }, Executable with { Signer = "Changed publisher" } })
        {
            platform.App = platform.App with { RegisteredTarget = Executable };
            var preview = service.Preview(SessionId, Metric, platform.App.Id);
            Assert.True(preview.CanExecute);
            platform.App = platform.App with { RegisteredTarget = changed };
            Assert.Throws<InvalidOperationException>(() => service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true));
        }
        Assert.Equal(0, platform.Calls);
    }

    [Fact]
    public async Task UnverifiedTrackingKeepsFurtherActionsAndNormalExitBlocked()
    {
        var platform = new Platform { LoseTracking = true };
        platform.App = platform.App with { ProductCode = null, RegisteredTarget = Executable, UninstallMethod = "registered_exe" };
        var gate = new LocalRemediationExecutionGate(); var service = Service(platform, gate: gate);
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        var operation = service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true);
        await platform.Launched.Task.WaitAsync(TimeSpan.FromSeconds(3));
        platform.Exit.SetResult(0);
        for (var attempt = 0; attempt < 600 && service.Find(operation.Id)!.Status == "running"; attempt++) await Task.Delay(5);
        Assert.Equal("unverified", service.Find(operation.Id)!.Status);
        Assert.Null(gate.TryEnter());
        Assert.False(gate.TryBeginShutdown());
    }

    [Fact]
    public async Task CurrentUserPackageUsesTheSamePreviewConsentAndAfterChecks()
    {
        var platform = new Platform();
        platform.App = platform.App with { ProductCode = null, SourceType = "store", UninstallMethod = "windows_package",
            PackageTarget = new("Example.Package_1.0_x64__publisher", "Example.Package_publisher", "S-1-5-21-1001") };
        var service = Service(platform);
        var preview = service.Preview(SessionId, Metric, platform.App.Id);
        Assert.True(preview.CanExecute);
        Assert.Contains("current account only", preview.Summary);
        var operation = service.Start(SessionId, Metric, platform.App.Id, preview.ApprovalToken, true);
        await platform.Launched.Task.WaitAsync(TimeSpan.FromSeconds(3));
        platform.Exit.SetResult(0);
        for (var attempt = 0; attempt < 600 && service.Find(operation.Id)!.Status == "running"; attempt++) await Task.Delay(5);
        Assert.Equal("removed", service.Find(operation.Id)!.Status);
        Assert.Equal("absent", service.Find(operation.Id)!.After!.InstallerStatus);
        Assert.Equal(1, platform.Calls);
    }

}
