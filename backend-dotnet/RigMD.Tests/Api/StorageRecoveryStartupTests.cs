using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Tests.Api;

public class StorageRecoveryStartupTests
{
    private sealed class BlockingBoot : IStorageBootIdentity
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new(false);
        public string? Value = "boot-1";
        public string? Read() { Started.SetResult(); Release.Wait(TimeSpan.FromSeconds(10)); return Value; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SlowBootQueryDoesNotBlockStartup_AndCannotPermitActionsBeforeRecovery(bool knownBoot)
    {
        var gate = new LocalRemediationExecutionGate();
        var boot = new BlockingBoot { Value = knownBoot ? "boot-1" : null };
        var journal = new StorageOperationJournal(new StorageOperationJournalTests.FaultStore(), boot, gate,
            TimeProvider.System, NullLogger<StorageOperationJournal>.Instance);
        using var service = new StorageRecoveryStartupService(journal, gate);
        try
        {
            await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
            await boot.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(StorageRecoveryStartupService.PendingMessage, gate.RecoveryBlockReason);
            Assert.Null(gate.TryEnter());
            Assert.Throws<InvalidOperationException>(journal.EnsureAvailable);
            boot.Release.Set();
            await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(3));
            if (knownBoot) { journal.EnsureAvailable(); using var lease = gate.TryEnter(); Assert.NotNull(lease); }
            else { Assert.NotNull(gate.RecoveryBlockReason); Assert.Null(gate.TryEnter()); Assert.Throws<InvalidOperationException>(journal.EnsureAvailable); }
        }
        finally { boot.Release.Set(); await service.StopAsync(CancellationToken.None); boot.Release.Dispose(); }
    }
}
