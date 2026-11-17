using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

internal sealed class CpuTemperatureSampler : IDisposable
{
    internal const string Source = "LibreHardwareMonitor 0.9.6 / PawnIO";
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan ProbeRetryInterval = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly Func<ICpuTemperatureProbe> _createProbe;
    private readonly Func<bool> _canAccessDriver;
    private readonly TimeProvider _clock;
    private ICpuTemperatureProbe? _probe;
    private CpuTemperatureReading? _reading;
    private long? _lastAttempt;
    private DateTimeOffset _nextProbeAt;
    private bool _disposed;

    internal CpuTemperatureSampler(Func<ICpuTemperatureProbe> createProbe, Func<bool> canAccessDriver,
        TimeProvider? clock = null)
    {
        _createProbe = createProbe;
        _canAccessDriver = canAccessDriver;
        _clock = clock ?? TimeProvider.System;
    }

    public CpuTemperatureReading? Read()
    {
        lock (_sync)
        {
            if (_disposed) return null;
            var timestamp = _clock.GetTimestamp();
            if (_lastAttempt.HasValue && _clock.GetElapsedTime(_lastAttempt.Value, timestamp) < SampleInterval)
                return _reading;

            _lastAttempt = timestamp;
            _reading = null;
            try
            {
                if (!_canAccessDriver())
                {
                    CloseProbe();
                    return null;
                }
                if (_clock.GetUtcNow() < _nextProbeAt) return null;
                _probe ??= _createProbe();
                var reading = CpuTemperatureSensorReader.SelectReading(_probe.Read(), Source);
                _reading = reading == null ? null : reading with { CapturedAtUtc = _clock.GetUtcNow() };
                if (_reading == null)
                {
                    CloseProbe();
                    _nextProbeAt = _clock.GetUtcNow() + ProbeRetryInterval;
                }
                return _reading;
            }
            catch
            {
                CloseProbe();
                _nextProbeAt = _clock.GetUtcNow() + ProbeRetryInterval;
                return null;
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            CloseProbe();
            _lastAttempt = null;
            _nextProbeAt = DateTimeOffset.MinValue;
        }
    }

    private void CloseProbe()
    {
        _reading = null;
        try { _probe?.Dispose(); }
        catch { /* Failure to release a stopped device must not retain its old reading. */ }
        _probe = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            CloseProbe();
        }
    }
}
