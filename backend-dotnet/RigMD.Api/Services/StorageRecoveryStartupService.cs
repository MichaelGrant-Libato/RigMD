namespace RigMD.Api.Services;

// Read-only screens can open while Windows restart information is queried.
// Every system mutation remains gated until recovery has been verified.
public sealed class StorageRecoveryStartupService : BackgroundService
{
    public const string PendingMessage = "RigMD is checking previous system actions. System changes are unavailable until this check finishes.";
    private readonly StorageOperationJournal _journal;

    public StorageRecoveryStartupService(StorageOperationJournal journal, LocalRemediationExecutionGate gate)
    {
        _journal = journal;
        gate.SetRecoveryBlock(PendingMessage);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // .NET 10 executes this on a background worker, outside the listener startup path.
        if (!stoppingToken.IsCancellationRequested) _journal.Initialize();
        return Task.CompletedTask;
    }
}
