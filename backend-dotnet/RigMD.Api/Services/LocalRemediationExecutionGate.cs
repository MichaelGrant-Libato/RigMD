namespace RigMD.Api.Services;

public sealed class LocalRemediationExecutionGate
{
    private int _running;
    private string? _recoveryBlockReason;
    public string? RecoveryBlockReason => Volatile.Read(ref _recoveryBlockReason);
    public void SetRecoveryBlock(string? reason) => Volatile.Write(ref _recoveryBlockReason, reason);

    // Atomically stop accepting actions before the desktop tears down its API process.
    public bool TryBeginShutdown() => Interlocked.CompareExchange(ref _running, 2, 0) is 0 or 2;

    public IDisposable? TryEnter()
    {
        if (RecoveryBlockReason != null) return null;
        return Interlocked.CompareExchange(ref _running, 1, 0) == 0 ? new Lease(this) : null;
    }

    private sealed class Lease(LocalRemediationExecutionGate owner) : IDisposable
    {
        private LocalRemediationExecutionGate? _owner = owner;
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref _owner, null);
            if (previous != null) Interlocked.CompareExchange(ref previous._running, 0, 1);
        }
    }
}
