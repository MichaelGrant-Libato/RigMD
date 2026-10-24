namespace RigMD.Infrastructure.Windows;

public sealed class CpuUsageSampler(Func<double> readCounter, TimeProvider? timeProvider = null)
{
    internal static double? BusyPercent(ulong oldIdle, ulong oldKernel, ulong oldUser, ulong idle, ulong kernel, ulong user)
    {
        if (idle < oldIdle || kernel < oldKernel || user < oldUser) return null;
        var total = (double)(kernel - oldKernel) + (user - oldUser);
        var idleDelta = idle - oldIdle;
        return total > 0 && idleDelta <= total ? (total - idleDelta) * 100 / total : null;
    }
    private readonly object _gate = new();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _primed;
    private long _lastTimestamp;
    private double? _lastValue;
    public DateTimeOffset? CapturedAt { get; private set; }
    public double? WindowSeconds { get; private set; }

    public double? Read()
    {
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            if (_primed && _time.GetElapsedTime(_lastTimestamp, now) < TimeSpan.FromSeconds(1))
                return _lastValue;
            try
            {
                var value = readCounter();
                if (!_primed || _time.GetElapsedTime(_lastTimestamp, now) > TimeSpan.FromSeconds(5))
                {
                    _primed = true;
                    _lastTimestamp = now;
                    _lastValue = null;
                    CapturedAt = null;
                    WindowSeconds = null;
                    return null; // Rate counters need two samples separated by a real interval.
                }
                WindowSeconds = _time.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
                _lastTimestamp = now;
                _lastValue = double.IsFinite(value) && value is >= 0 and <= 100 ? value : null;
                CapturedAt = _lastValue.HasValue ? _time.GetUtcNow() : null;
                if (!_lastValue.HasValue) WindowSeconds = null;
                return _lastValue;
            }
            catch
            {
                _primed = false;
                _lastValue = null;
                CapturedAt = null;
                WindowSeconds = null;
                return null;
            }
        }
    }
}
