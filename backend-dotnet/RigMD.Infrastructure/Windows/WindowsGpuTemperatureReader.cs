using System.Runtime.InteropServices;

namespace RigMD.Infrastructure.Windows;

internal static class WindowsGpuTemperatureReader
{
    public const string Source = "Windows display-driver sensor";
    private const int AdapterPerformanceData = 62;

    public static double? Read(ulong adapterLuid)
    {
        var adapter = new OpenAdapter
        {
            LuidLow = (uint)adapterLuid,
            LuidHigh = unchecked((int)(adapterLuid >> 32))
        };
        try
        {
            if (D3DKMTOpenAdapterFromLuid(ref adapter) < 0) return null;
            try
            {
                var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AdapterPerformance>());
                try
                {
                    // Physical adapter zero is the main sensor of this logical adapter.
                    Marshal.StructureToPtr(new AdapterPerformance(), buffer, false);
                    var query = new QueryAdapter
                    {
                        Handle = adapter.Handle,
                        Type = AdapterPerformanceData,
                        Data = buffer,
                        Size = (uint)Marshal.SizeOf<AdapterPerformance>()
                    };
                    var status = D3DKMTQueryAdapterInfo(ref query);
                    var performance = Marshal.PtrToStructure<AdapterPerformance>(buffer);
                    return DecodeTemperature(status, performance.Temperature);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                var close = new CloseAdapter { Handle = adapter.Handle };
                D3DKMTCloseAdapter(ref close);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    internal static double? DecodeTemperature(int status, uint rawTemperature)
    {
        // D3DKMT reports tenths of a degree Celsius. Some unsupported drivers return all zeros.
        return status >= 0 && rawTemperature is > 0 and <= 1500 ? rawTemperature / 10d : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter
    {
        public uint LuidLow;
        public int LuidHigh;
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapter
    {
        public uint Handle;
        public int Type;
        public nint Data;
        public uint Size;
    }

    // D3DKMT_ADAPTER_PERFDATA uses 8-byte alignment for its 64-bit fields on both architectures.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct AdapterPerformance
    {
        [FieldOffset(0)] public uint PhysicalAdapterIndex;
        [FieldOffset(8)] public ulong MemoryFrequency;
        [FieldOffset(16)] public ulong MaxMemoryFrequency;
        [FieldOffset(24)] public ulong MaxMemoryFrequencyOc;
        [FieldOffset(32)] public ulong MemoryBandwidth;
        [FieldOffset(40)] public ulong PcieBandwidth;
        [FieldOffset(48)] public uint FanRpm;
        [FieldOffset(52)] public uint Power;
        [FieldOffset(56)] public uint Temperature;
        [FieldOffset(60)] public byte PowerState;
    }

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapter query);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);
}
