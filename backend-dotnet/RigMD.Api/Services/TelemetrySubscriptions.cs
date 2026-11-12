namespace RigMD.Api.Services;

public sealed class TelemetrySubscriptions
{
    private readonly object _gate = new();
    private readonly HashSet<string> _connections = new(StringComparer.Ordinal);
    private TaskCompletionSource _available = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Add(string connectionId)
    {
        lock (_gate) { if (_connections.Add(connectionId)) _available.TrySetResult(); }
    }
    public void Remove(string connectionId)
    {
        lock (_gate) { if (_connections.Remove(connectionId) && _connections.Count == 0) _available = NewSignal(); }
    }
    public Task WaitAsync(CancellationToken token)
    {
        lock (_gate) return (_connections.Count > 0 ? Task.CompletedTask : _available.Task).WaitAsync(token);
    }
}
