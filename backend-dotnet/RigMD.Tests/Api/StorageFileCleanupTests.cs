using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class StorageFileCleanupTests
{
    [Fact]
    public void RecycleBinStructuresMatchTheWindowsX64AndX86Abi()
    {
        var x64 = typeof(StorageCleanupWorker).GetNestedType("BinInfo64", System.Reflection.BindingFlags.NonPublic)!;
        var x86 = typeof(StorageCleanupWorker).GetNestedType("BinInfo32", System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(24, System.Runtime.InteropServices.Marshal.SizeOf(x64));
        Assert.Equal(8, System.Runtime.InteropServices.Marshal.OffsetOf(x64, "Bytes").ToInt32());
        Assert.Equal(20, System.Runtime.InteropServices.Marshal.SizeOf(x86));
        Assert.Equal(4, System.Runtime.InteropServices.Marshal.OffsetOf(x86, "Bytes").ToInt32());
    }
    private static readonly Guid Session = Guid.NewGuid();
    private const string Metric = "storage_usage_C:";
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Platform : IStorageCleanupPlatform
    {
        public string? DesktopUser { get; set; } = "same-desktop-user";
        public StorageFileTarget File { get; set; } = new("documents", @"C:\Users\Example\Documents", "notes.pdf", 100, 123, "volume:file-id");
        public RecycleBinReading Bin { get; set; } = new(200, 2);
        public long Free { get; set; } = 10;
        public string FilePresence { get; set; } = "present";
        public int Calls;
        public bool LoseTracking;
        public StorageCleanupTarget? Target;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public (StorageFileTarget[], bool) Scan(string category, string drive, CancellationToken token) => ([File], false);
        public StorageFileTarget Inspect(StorageFileTarget file) => File;
        public string Presence(StorageFileTarget file) => FilePresence;
        public StorageVolumeReading[] ReadVolumes() => [new("C:\\", Free, 100)];
        public RecycleBinReading ReadBin(string drive) => Bin;
        public async Task<int> ExecuteAsync(StorageCleanupTarget target)
        {
            Target = target; Calls++; Started.SetResult();
            var result = await Finish.Task;
            if (LoseTracking) throw new UninstallTrackingException(new IOException("Simulated lost helper connection"));
            return result;
        }
    }
    private static StorageFileCleanupService Service(Platform platform, Clock? clock = null, LocalRemediationExecutionGate? gate = null, StorageOperationJournal? journal = null)
    {
        clock ??= new(); return new(platform, new RemediationApprovalStore(clock), gate ?? new(), clock, journal);
    }

    [Fact]
    public async Task CompletedCleanupReceiptSurvivesRestartWithoutReplayingOrPersistingApproval()
    {
        var store = new StorageOperationJournalTests.FaultStore(); var gate = new LocalRemediationExecutionGate();
        var journal = StorageOperationJournalTests.Journal(store, gate);
        var platform = new Platform(); var service = Service(platform, gate: gate, journal: journal);
        var preview = service.Preview(Session, Metric, "empty_recycle_bin", []);
        var operation = service.Start(Session, Metric, preview.ApprovalToken, true);
        await platform.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        platform.Bin = new(0, 0); platform.Free = 20; platform.Finish.SetResult(0);
        for (var i = 0; i < 600 && service.Find(operation.Id)!.Status == "running"; i++) await Task.Delay(5);
        Assert.Equal("emptied", service.Find(operation.Id)!.Status);
        Assert.DoesNotContain(preview.ApprovalToken!, store.Entries[operation.Id].Payload);
        gate = new(); var restored = StorageOperationJournalTests.Journal(store, gate); var other = new Platform();
        var next = Service(other, gate: gate, journal: restored);
        Assert.Equal("emptied", next.Find(operation.Id)!.Status); Assert.Single(next.Recent(Session, Metric)); Assert.Empty(next.Recent(Guid.NewGuid(), Metric));
        Assert.Equal(0, other.Calls); Assert.Null(next.Find(operation.Id)!.Before.ApprovalToken); Assert.Null(gate.RecoveryBlockReason);
    }

    [Fact]
    public void RecycleSinkRejectsPermanentDeleteAndMissingRecycleConfirmation()
    {
        var type = typeof(WindowsRecycleOperation).GetNestedType("RecycleSink", System.Reflection.BindingFlags.NonPublic)!;
        var sink = (WindowsRecycleOperation.IProgressSink)Activator.CreateInstance(type,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, [new Platform().File, @"C:\Users\Example\Documents\notes.pdf"], null)!;
        Assert.True(sink.PreDeleteItem(0, null!) < 0);
        Assert.True(sink.PostDeleteItem(0x80, null!, 0, null) < 0);
        Assert.True(sink.PreMoveItem(0, null!, null!, "other") < 0);
        Assert.True(sink.PreCopyItem(0, null!, null!, "other") < 0);
    }

    [Fact]
    public async Task LostCleanupTrackingBlocksFurtherSystemActionsAndExit()
    {
        var platform = new Platform { LoseTracking = true }; var gate = new LocalRemediationExecutionGate(); var service = Service(platform, gate: gate);
        var preview = service.Preview(Session, Metric, "empty_recycle_bin", []);
        var operation = service.Start(Session, Metric, preview.ApprovalToken, true);
        await platform.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        platform.Finish.SetResult(0);
        for (var i = 0; i < 600 && service.Find(operation.Id)!.Status == "running"; i++) await Task.Delay(5);
        Assert.Equal("unverified", service.Find(operation.Id)!.Status);
        Assert.Null(gate.TryEnter()); Assert.False(gate.TryBeginShutdown());
    }

    [Theory]
    [InlineData(@"..\report.pdf")]
    [InlineData(@"C:\Users\Example\report.pdf")]
    [InlineData(@"\\host\report.pdf")]
    [InlineData(@"folder\..\report.pdf")]
    [InlineData(@"report.pdf:stream")]
    [InlineData(@"node_modules\data.txt")]
    [InlineData(@".git\description.txt")]
    [InlineData(@"startup\notes.txt")]
    [InlineData(@"folder\secret.pem")]
    [InlineData(@"script.ps1")]
    [InlineData(@"config.json")]
    [InlineData(@"program.exe")]
    [InlineData(@"folder/notes.txt")]
    public void TraversalCommandsAndApplicationDataAreNotPersonalFileCleanupTargets(string name)
    {
        Assert.False(StorageFilePolicy.EligibleName(name));
        Assert.Throws<InvalidOperationException>(() => StorageFilePolicy.Resolve(@"C:\Users\Example\Documents", name));
    }
    [Theory]
    [InlineData("report.pdf")]
    [InlineData(@"Sound Recordings\recording.m4a")]
    [InlineData(@"Backups\archive.zip")]
    public void OrdinaryPersonalContentCanBeSelectedButNeverAutoDeleted(string name)
    {
        Assert.True(StorageFilePolicy.EligibleName(name));
        Assert.StartsWith(@"C:\Users\Example\Documents\", StorageFilePolicy.Resolve(@"C:\Users\Example\Documents", name));
    }
    [Fact]
    public void FileHandlesDetectReplacementAtTheSameNameWithoutReadingContents()
    {
        var root = Path.Combine(Path.GetTempPath(), "RigMD-cleanup-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "example.txt");
        try
        {
            File.WriteAllText(path, "original");
            var first = StorageFilePolicy.Inspect("documents", root, "example.txt");
            File.Move(path, Path.Combine(root, "original.txt"));
            File.WriteAllText(path, "original");
            var second = StorageFilePolicy.Inspect("documents", root, "example.txt");
            Assert.NotEqual(first.FileIdentity, second.FileIdentity);
            Assert.Equal(first.Bytes, second.Bytes);
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void OnlyCatalogIdsCanBePreviewedAndConsentIsRequired()
    {
        var platform = new Platform(); var service = Service(platform);
        Assert.Throws<InvalidOperationException>(() => service.Preview(Session, Metric, "recycle_files", [@"C:\Windows\file.txt"]));
        Assert.Throws<InvalidOperationException>(() => service.Preview(Session, Metric, "recycle_files", []));
        var file = service.Files(Metric, "documents", CancellationToken.None).Files.Single();
        Assert.Equal("notes.pdf", file.Name);
        var preview = service.Preview(Session, Metric, "recycle_files", [file.Id]);
        Assert.True(preview.CanExecute);
        Assert.Contains("does not free", preview.Summary);
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, false));
        Assert.Equal(0, platform.Calls);
    }
    [Fact]
    public void FileIdentityAccountSessionExpiryAndCurrentWarningCannotBeBypassed()
    {
        var platform = new Platform(); var clock = new Clock(); var service = Service(platform, clock);
        CleanupPreview Preview() { var id = service.Files(Metric, "documents", CancellationToken.None).Files.Single().Id; return service.Preview(Session, Metric, "recycle_files", [id]); }
        var preview = Preview(); platform.File = platform.File with { FileIdentity = "replacement" };
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
        preview = Preview(); platform.DesktopUser = "another-user";
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
        platform.DesktopUser = "same-desktop-user"; preview = Preview();
        Assert.Throws<InvalidOperationException>(() => service.Start(Guid.NewGuid(), Metric, preview.ApprovalToken, true));
        preview = Preview(); clock.Now = clock.Now.AddMinutes(6);
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
        preview = Preview(); platform.Free = 50;
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
        Assert.False(Preview().CanExecute);
        Assert.Equal(0, platform.Calls);
    }
    [Fact]
    public void EmptyingTheBinNeedsItsOwnApprovalAndAChangedBinRequiresANewPreview()
    {
        var platform = new Platform(); var service = Service(platform);
        var preview = service.Preview(Session, Metric, "empty_recycle_bin", []);
        Assert.True(preview.CanExecute);
        Assert.Contains("ALL 2 items", preview.Summary);
        Assert.Contains("outside RigMD", preview.Summary);
        platform.Bin = new(300, 3);
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
        platform.Bin = new(0, 0);
        Assert.False(service.Preview(Session, Metric, "empty_recycle_bin", []).CanExecute);
        Assert.Equal(0, platform.Calls);
    }
    [Theory]
    [InlineData("recycle_files", 0, "absent", 3, "recycled")]
    [InlineData("recycle_files", 0, "unknown", 3, "unverified")]
    [InlineData("recycle_files", 13, "absent", 3, "partial")]
    [InlineData("recycle_files", 1223, "present", 2, "canceled")]
    [InlineData("empty_recycle_bin", 0, "present", 0, "emptied")]
    [InlineData("empty_recycle_bin", 0, "present", 2, "unverified")]
    public async Task ResultRequiresWindowsCompletionAndFreshPostEvidence(string kind, int code, string presence, int binItems, string status)
    {
        var platform = new Platform(); var gate = new LocalRemediationExecutionGate(); var service = Service(platform, gate: gate);
        var ids = kind == "recycle_files" ? new[] { service.Files(Metric, "documents", CancellationToken.None).Files.Single().Id } : [];
        var preview = service.Preview(Session, Metric, kind, ids);
        var operation = service.Start(Session, Metric, preview.ApprovalToken, true);
        await platform.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(gate.TryEnter()); Assert.False(gate.TryBeginShutdown());
        Assert.Equal(kind, platform.Target!.Kind);
        platform.FilePresence = presence; platform.Bin = new(100 * binItems, binItems); platform.Free = kind == "empty_recycle_bin" ? 30 : 10;
        platform.Finish.SetResult(code);
        for (var i = 0; i < 600 && service.Find(operation.Id)!.Status == "running"; i++) await Task.Delay(5);
        var completed = service.Find(operation.Id)!;
        Assert.Equal(status, completed.Status);
        Assert.Equal(platform.Free, completed.AfterFreeBytes);
        Assert.Equal(1, platform.Calls);
        Assert.Throws<InvalidOperationException>(() => service.Start(Session, Metric, preview.ApprovalToken, true));
    }
}
