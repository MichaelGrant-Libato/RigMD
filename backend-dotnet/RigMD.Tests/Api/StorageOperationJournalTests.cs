using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Api;

public sealed class StorageOperationJournalTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "rigmd-journal-test-" + Guid.NewGuid().ToString("N") + ".db");
    private SqliteStorageOperationStore Store() => new($"Data Source={_path};Pooling=False");
    public void Dispose() { foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix); }
    private sealed class Boot(string? value) : IStorageBootIdentity { public string? Read() => value; }
    internal sealed class FaultStore : IStorageOperationStore
    {
        public Dictionary<string, StorageOperationEntry> Entries = new(); public int Saves; public int FailOn; public bool FailRead;
        public void Initialize() { }
        public StorageOperationEntry[] ReadAll() => FailRead ? throw new IOException() : Entries.Values.ToArray();
        public void Save(StorageOperationEntry entry) { if (++Saves == FailOn) throw new IOException(); Entries[entry.Id] = entry; }
    }
    internal static StorageOperationJournal Journal(IStorageOperationStore store, LocalRemediationExecutionGate gate, string? boot = "boot-1")
    {
        var journal = new StorageOperationJournal(store, new Boot(boot), gate, TimeProvider.System, NullLogger<StorageOperationJournal>.Instance);
        journal.Initialize(); return journal;
    }
    private static readonly Guid Session = Guid.NewGuid();
    private const string Metric = "storage_usage_C:";
    private static AppUninstallOperation Operation() => new(Guid.NewGuid().ToString("N"), Session, Metric,
        new("Disposable app", "Test publisher", "1", 100, "Current account") { Id = new('a', 64), CanUninstall = true, ProductCode = "{12345678-1234-1234-1234-123456789ABC}" },
        "running", "Testing", new(DateTimeOffset.UtcNow, "installed", [new("C:\\", 10, 100)]));

    [Fact]
    public void InterruptedReceiptSurvivesMultipleRestartsAndNeverBecomesSuccessOrAnApproval()
    {
        var operation = Operation(); var journal = Journal(Store(), new());
        journal.Save("uninstall", operation.Id, Session, Metric, operation, true);
        for (var restart = 0; restart < 2; restart++)
        {
            var gate = new LocalRemediationExecutionGate(); journal = Journal(Store(), gate);
            Assert.Equal("unverified", journal.Find<AppUninstallOperation>("uninstall", operation.Id)!.Status);
            Assert.Null(gate.TryEnter()); Assert.Throws<InvalidOperationException>(() => journal.EnsureAvailable());
            Assert.True(gate.TryBeginShutdown()); // A new UI can close without interrupting the old orphan.
        }
        var nextGate = new LocalRemediationExecutionGate(); var afterBoot = Journal(Store(), nextGate, "boot-2");
        Assert.Equal("unverified", afterBoot.Find<AppUninstallOperation>("uninstall", operation.Id)!.Status);
        afterBoot.EnsureAvailable(); using var lease = nextGate.TryEnter(); Assert.NotNull(lease);
        Assert.DoesNotContain("ProductCode", Store().ReadAll().Single().Payload, StringComparison.OrdinalIgnoreCase);
        Assert.Null(afterBoot.Find<AppUninstallOperation>("cleanup", operation.Id));
    }
    [Fact]
    public void CompletedReceiptCanBeReadByNewServiceWithoutLaunchingOrResolvingAnApp()
    {
        var operation = Operation(); var snapshot = operation.Before with { InstallerStatus = "absent" };
        var completed = AppUninstallService.Assess(operation, 0, snapshot);
        var journal = Journal(Store(), new()); journal.Save("uninstall", operation.Id, Session, Metric, completed, false);
        var gate = new LocalRemediationExecutionGate(); var restored = Journal(Store(), gate);
        var platform = new Platform(); var service = new AppUninstallService(platform, new(TimeProvider.System), gate, TimeProvider.System, NullLogger<AppUninstallService>.Instance, restored);
        Assert.Equal("removed", service.Find(operation.Id)!.Status); Assert.Single(service.Recent(Session)); Assert.Empty(service.Recent(Guid.NewGuid()));
        Assert.Equal(0, platform.Calls); Assert.Null(gate.RecoveryBlockReason);
    }
    [Theory]
    [InlineData(null)] [InlineData("boot-1")]
    public void UnknownOrUnchangedBootCannotClearInterruptedAction(string? boot)
    {
        var operation = Operation(); var store = new FaultStore();
        Journal(store, new()).Save("uninstall", operation.Id, Session, Metric, operation, true);
        var gate = new LocalRemediationExecutionGate(); Journal(store, gate, boot);
        Assert.Null(gate.TryEnter()); Assert.True(store.Entries[operation.Id].MayBeRunning);
    }
    [Fact]
    public void UnreadableOrCorruptHistoryBlocksChangesWithoutExecutingAnything()
    {
        var store = new FaultStore { FailRead = true }; var gate = new LocalRemediationExecutionGate(); var journal = Journal(store, gate);
        Assert.Null(gate.TryEnter()); Assert.Throws<InvalidOperationException>(journal.EnsureAvailable);
        store.FailRead = false; var operation = Operation();
        store.Entries[operation.Id] = new(operation.Id, "uninstall", Session, Metric, "{broken", "boot-1", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        gate = new(); journal = Journal(store, gate); Assert.Null(gate.TryEnter()); Assert.Throws<InvalidOperationException>(journal.EnsureAvailable);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task IntentMustBeSavedBeforeAnyWindowsAction(int failOn)
    {
        var store = new FaultStore { FailOn = failOn }; var gate = new LocalRemediationExecutionGate(); var journal = Journal(store, gate);
        var platform = new Platform(); var service = new AppUninstallService(platform, new(TimeProvider.System), gate, TimeProvider.System, NullLogger<AppUninstallService>.Instance, journal);
        var preview = service.Preview(Session, Metric, platform.App.Id);
        if (failOn == 1) Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, platform.App.Id, preview.ApprovalToken, true));
        else
        {
            var operation = service.Start(Session, Metric, platform.App.Id, preview.ApprovalToken, true);
            for (var i = 0; i < 600 && service.Find(operation.Id)!.Status == "running"; i++) await Task.Delay(5);
            Assert.Equal("unverified", service.Find(operation.Id)!.Status);
        }
        Assert.Equal(0, platform.Calls); Assert.Null(gate.TryEnter());
    }
    [Fact]
    public async Task FailureSavingFinalReceiptPreservesObservedOutcomeAndBlocksBlindRetry()
    {
        var store = new FaultStore { FailOn = 3 }; var gate = new LocalRemediationExecutionGate(); var journal = Journal(store, gate);
        var platform = new Platform(); var service = new AppUninstallService(platform, new(TimeProvider.System), gate, TimeProvider.System, NullLogger<AppUninstallService>.Instance, journal);
        var preview = service.Preview(Session, Metric, platform.App.Id);
        var operation = service.Start(Session, Metric, platform.App.Id, preview.ApprovalToken, true);
        for (var i = 0; i < 600 && service.Find(operation.Id)!.Status == "running"; i++) await Task.Delay(5);
        Assert.Equal(1, platform.Calls); Assert.Equal("removed", service.Find(operation.Id)!.Status); Assert.NotNull(service.Find(operation.Id)!.PersistenceWarning);
        Assert.True(store.Entries[operation.Id].MayBeRunning); Assert.Null(gate.TryEnter());
        var recoveredGate = new LocalRemediationExecutionGate(); var recovered = Journal(store, recoveredGate);
        Assert.Equal("unverified", recovered.Find<AppUninstallOperation>("uninstall", operation.Id)!.Status); Assert.Null(recoveredGate.TryEnter());
    }
    [Fact]
    public void StoreRejectsIdentityChangesAndRetainsInterruptedRowsWhenPruning()
    {
        var store = Store(); store.Initialize(); var at = DateTimeOffset.UtcNow;
        var entry = new StorageOperationEntry(Guid.NewGuid().ToString("N"), "uninstall", Session, Metric, "{}", "boot-1", true, at, at);
        store.Save(entry); Assert.Throws<InvalidOperationException>(() => store.Save(entry with { SessionId = Guid.NewGuid() }));
        for (var i = 0; i < 203; i++) store.Save(entry with { Id = Guid.NewGuid().ToString("N"), MayBeRunning = false, CreatedAt = at.AddSeconds(i) });
        Assert.Equal(201, store.ReadAll().Length); Assert.Contains(store.ReadAll(), item => item.Id == entry.Id && item.MayBeRunning);
    }

    [Fact]
    public void ConcurrentStoresCannotCommitTwoActiveStorageIntents()
    {
        var store = Store(); store.Initialize(); var at = DateTimeOffset.UtcNow;
        var first = new StorageOperationEntry(Guid.NewGuid().ToString("N"), "uninstall", Session, Metric, "{}", "boot-1", true, at, at);
        store.Save(first);
        var second = first with { Id = Guid.NewGuid().ToString("N"), Kind = "cleanup" };
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => Store().Save(second));
        store.Save(first with { MayBeRunning = false }); Store().Save(second);
        Assert.Single(store.ReadAll(), item => item.MayBeRunning);
    }

    private sealed class Platform : IAppUninstallPlatform
    {
        public StorageAppEntry App { get; } = Operation().App; public int Calls; private bool _removed;
        public bool IsAdministrator => true;
        public StorageAppSnapshot Capture() => new(DateTimeOffset.UtcNow, [App], false, "Fixture");
        public InstallerEvidence Inspect(string productCode) => _removed ? new("absent") : new("installed", App.Name, App.Publisher, App.Version);
        public StorageVolumeReading[] ReadVolumes() => [new("C:\\", _removed ? 25 : 10, 100)];
        public Task<int> UninstallAsync(string productCode) { Calls++; _removed = true; return Task.FromResult(0); }
    }
}
