using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Controllers;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class StorageOverviewTests
{
    [Theory]
    [InlineData("storage_usage_C:", "C:\\")]
    [InlineData("storage_usage_d:\\", "D:\\")]
    [InlineData("storage_usage", "C:\\")]
    [InlineData("storage_usage_C:\\Windows", null)]
    [InlineData("C:\\Windows", null)]
    [InlineData("storage_usage_../../", null)]
    [InlineData("storage_usage_C:\n", null)]
    [InlineData("", null)]
    public void OnlyStorageMetricsSelectFixedDriveRoots(string metric, string? expected)
        => Assert.Equal(expected, StorageOverviewReader.DriveForMetric(metric, "C:\\"));

    [Theory]
    [InlineData((int)FileAttributes.ReparsePoint)]
    [InlineData((int)FileAttributes.Offline)]
    [InlineData(0x40000)]
    [InlineData(0x400000)]
    public void LinksAndCloudRecallAttributesAreExcluded(int attributes)
    {
        Assert.True(StorageOverviewReader.MustSkip((FileAttributes)attributes));
        Assert.False(StorageOverviewReader.MustSkip(FileAttributes.Normal));
    }

    [Fact]
    public void FolderMeasurementCountsMetadataAndKeepsOnlyFiveLargestFiles()
    {
        using var fixture = new FolderFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
        for (var index = 1; index <= 8; index++) File.WriteAllBytes(Path.Combine(fixture.Root, "nested", $"file-{index}.txt"), new byte[index * 10]);
        var remaining = 100;
        var reading = StorageOverviewReader.MeasureFolder(fixture.Root, ref remaining, Stopwatch.StartNew(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.False(reading.Partial);
        Assert.Equal(360L, reading.FileBytes);
        Assert.Equal(8, reading.FilesCounted);
        Assert.Equal(new long[] { 80, 70, 60, 50, 40 }, reading.LargestFiles.Select(file => file.Bytes));
        Assert.All(reading.LargestFiles, file => Assert.False(Path.IsPathFullyQualified(file.Name)));
        Assert.Equal(8, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void MissingFoldersStayUnknownWhileAnEmptyReadableFolderCanBeZero()
    {
        using var fixture = new FolderFixture();
        var remaining = 100;
        var watch = Stopwatch.StartNew();
        var missing = StorageOverviewReader.MeasureFolder(Path.Combine(fixture.Root, "missing"), ref remaining, watch, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Null(missing.FileBytes);
        Assert.True(missing.Partial);
        var empty = StorageOverviewReader.MeasureFolder(fixture.Root, ref remaining, watch, TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.Equal(0L, empty.FileBytes);
        Assert.False(empty.Partial);
    }

    [Fact]
    public void EntryAndTimeLimitsProducePartialCountsRatherThanCompleteEmptyClaims()
    {
        using var fixture = new FolderFixture();
        for (var index = 0; index < 4; index++) File.WriteAllBytes(Path.Combine(fixture.Root, $"file-{index}.bin"), new byte[20]);
        var remaining = 2;
        var partial = StorageOverviewReader.MeasureFolder(fixture.Root, ref remaining, Stopwatch.StartNew(), TimeSpan.FromSeconds(10), CancellationToken.None);
        Assert.True(partial.Partial);
        Assert.Equal(2, partial.FilesCounted);
        Assert.Equal(40L, partial.FileBytes);
        remaining = 100;
        var expired = StorageOverviewReader.MeasureFolder(fixture.Root, ref remaining, Stopwatch.StartNew(), TimeSpan.Zero, CancellationToken.None);
        Assert.True(expired.Partial);
        Assert.Equal(0, expired.FilesCounted);
    }

    [Fact]
    public void CancellationStopsAReview()
    {
        using var fixture = new FolderFixture();
        var remaining = 100;
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => StorageOverviewReader.MeasureFolder(fixture.Root, ref remaining, Stopwatch.StartNew(), TimeSpan.FromSeconds(10), source.Token));
    }

    [Theory]
    [InlineData("storage_usage_C:\\Windows", 400)]
    [InlineData("storage_usage_D:", 404)]
    public async Task InvalidOrMissingDrivesNeverTriggerAnUninstallOrInspectOtherDrives(string metric, int expected)
    {
        var platform = new ReadOnlyPlatform();
        var controller = new StorageOverviewController(platform) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Get(metric, CancellationToken.None));
        Assert.Equal(expected, result.StatusCode);
        Assert.Equal(expected == 400 ? 0 : 1, platform.Reads);
    }

    private sealed class ReadOnlyPlatform : IAppUninstallPlatform
    {
        public int Reads { get; private set; }
        public bool IsAdministrator => throw new InvalidOperationException("A read must not need elevation.");
        public StorageAppSnapshot Capture() => throw new InvalidOperationException("No app inventory is needed for drive selection.");
        public InstallerEvidence Inspect(string code) => throw new InvalidOperationException("A read must not inspect an installer.");
        public StorageVolumeReading[] ReadVolumes() { Reads++; return [new("C:\\", 100, 1000)]; }
        public Task<int> UninstallAsync(string code) => throw new InvalidOperationException("A storage read must never uninstall an app.");
    }

    private sealed class FolderFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("rigmd-storage-review-tests-").FullName;
        public void Dispose()
        {
            var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Root).StartsWith("rigmd-storage-review-tests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test fixture location.");
            Directory.Delete(Root, true);
        }
    }
}
