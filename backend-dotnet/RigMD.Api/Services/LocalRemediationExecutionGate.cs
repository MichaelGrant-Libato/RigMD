namespace RigMD.Api.Services;

public sealed class LocalRemediationExecutionGate
{
    private int _running;

    public IDisposable? TryEnter()
    {
        return Interlocked.CompareExchange(ref _running, 1, 0) == 0 ? new Lease(this) : null;
    }

    private sealed class Lease(LocalRemediationExecutionGate owner) : IDisposable
    {
        private LocalRemediationExecutionGate? _owner = owner;
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref _owner, null);
            if (previous != null) Interlocked.Exchange(ref previous._running, 0);
        }
    }
}
