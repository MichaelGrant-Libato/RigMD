using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Infrastructure.Windows;

/// <summary>A local PDH query that preserves the difference between a valid zero and missing data.</summary>
public sealed class WindowsCounterQuery : IDisposable
{
    private readonly QueryHandle _query;
    private readonly nint[] _counters;

    public WindowsCounterQuery(params string[] paths)
    {
        _counters = new nint[paths.Length];
        if (PdhOpenQueryW(null, 0, out _query) != 0) return;
        for (var i = 0; i < paths.Length; i++)
            if (PdhAddEnglishCounterW(_query, paths[i], 0, out var counter) == 0) _counters[i] = counter;
    }

    public bool Collect() => !_query.IsInvalid && PdhCollectQueryData(_query) == 0;

    public double? Read(int index)
    {
        const uint formatDoubleWithoutPercentCap = 0x200 | 0x8000;
        if (_counters[index] == 0 ||
            PdhGetFormattedCounterValue(_counters[index], formatDoubleWithoutPercentCap, 0, out var reading) != 0 ||
            reading.Status is not (0 or 1) || !double.IsFinite(reading.Value) || reading.Value < 0)
            return null;
        return reading.Value;
    }

    public void Dispose() => _query.Dispose();

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct FormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    private sealed class QueryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public QueryHandle() : base(true) { }
        protected override bool ReleaseHandle() => PdhCloseQuery(handle) == 0;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhOpenQueryW(string? source, nuint userData, out QueryHandle query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhAddEnglishCounterW(QueryHandle query, string path, nuint userData, out nint counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCollectQueryData(QueryHandle query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, nint type, out FormattedValue value);

    [DllImport("pdh.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint PdhCloseQuery(nint query);
}
