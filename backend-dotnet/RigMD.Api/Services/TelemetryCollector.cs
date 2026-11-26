using RigMD.Application.Models;

namespace RigMD.Api.Services;

internal sealed class TelemetryCollector<T>(string name, Func<T?> read, ILogger logger, TimeProvider? clock = null) where T : class
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(5);
    private Task<(T? Value, DateTimeOffset At)>? _pending;
    private DateTimeOffset _startedAt;
    private DateTimeOffset? _lastSuccess;
    private T? _current;
    private bool _attempted;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public T? Read(DateTimeOffset now, CancellationToken token)
    {
        if (_pending is { IsCompletedSuccessfully: true } finished)
        {
            var (value, at) = finished.Result;
            _current = at - _startedAt <= FreshFor ? value : null;
            if (_current is not null) _lastSuccess = at;
            _pending = null;
            _attempted = true;
        }
        if (_pending is null && !token.IsCancellationRequested)
        {
            _startedAt = now;
            _pending = Task.Run(() =>
            {
                try { return (read(), _clock.GetUtcNow()); }
                catch (Exception ex)
                {
                    logger.LogDebug("Telemetry collector {Collector} did not return a reading ({ErrorType}).", name, ex.GetType().Name);
                    return (null, _clock.GetUtcNow());
                }
            });
        }
        return IsFresh(now) ? _current : null;
    }

    private bool IsFresh(DateTimeOffset now) =>
        _current is not null && _lastSuccess is { } at && now - at <= FreshFor && at - now <= TimeSpan.FromSeconds(2);

    public TelemetryCollectorStatus Status(DateTimeOffset now) => new(name,
        IsFresh(now) ? "live" :
        _pending != null && now - _startedAt >= FreshFor ? "timed out" :
        _lastSuccess.HasValue ? "stale" : _attempted ? "unavailable" : "collecting", _lastSuccess);

    public void AfterPending(Action cleanup)
    {
        if (_pending is null || _pending.IsCompleted) cleanup();
        else _ = _pending.ContinueWith(_ => cleanup(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
