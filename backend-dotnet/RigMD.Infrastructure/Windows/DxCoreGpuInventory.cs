using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace RigMD.Infrastructure.Windows;

internal sealed record GpuAdapterIdentity(ulong Luid, string Name, string Type, bool IsHardware = true)
{
    public double? DedicatedMemoryGb { get; init; }
    public double? SharedMemoryGb { get; init; }
    public string? DriverVersion { get; init; }
}

internal static class DxCoreGpuInventory
{
    private static readonly Guid D3D11Graphics = new("8c47866b-7583-450d-f0f0-6bada895af4b");

    public static IReadOnlyList<GpuAdapterIdentity> Read()
    {
        var identities = new List<GpuAdapterIdentity>();
        IAdapterFactory? factory = null;
        IAdapterList? list = null;
        try
        {
            if (DXCoreCreateAdapterFactory(typeof(IAdapterFactory).GUID, out factory) < 0 || factory is null)
                return identities;
            if (factory.CreateAdapterList(1, D3D11Graphics, typeof(IAdapterList).GUID, out list) < 0 || list is null)
                return identities;

            var count = list.GetAdapterCount();
            for (uint index = 0; index < count; index++)
            {
                IAdapter? adapter = null;
                try
                {
                    if (list.GetAdapter(index, typeof(IAdapter).GUID, out adapter) < 0 || adapter is null)
                        continue;
                    var hardware = ReadProperty(adapter, AdapterProperty.IsHardware);
                    if (hardware is not [0] and not [1])
                        continue;
                    var luid = ReadProperty(adapter, AdapterProperty.InstanceLuid);
                    if (luid.Length != sizeof(ulong))
                        continue;

                    var description = ReadProperty(adapter, AdapterProperty.DriverDescription);
                    var terminator = Array.IndexOf(description, (byte)0);
                    var name = Encoding.UTF8.GetString(description, 0, terminator < 0 ? description.Length : terminator).Trim();
                    var type = ReadProperty(adapter, AdapterProperty.IsIntegrated) switch
                    {
                        [1] => "Integrated",
                        [0] => "Dedicated",
                        _ => "Unknown"
                    };
                    var dedicated = ReadUInt64(adapter, AdapterProperty.DedicatedAdapterMemory);
                    var reserved = ReadUInt64(adapter, AdapterProperty.DedicatedSystemMemory);
                    var version = ReadUInt64(adapter, AdapterProperty.DriverVersion);
                    identities.Add(new(BinaryPrimitives.ReadUInt64LittleEndian(luid),
                        string.IsNullOrEmpty(name) ? "Graphics adapter" : name, type, hardware[0] == 1)
                    {
                        DedicatedMemoryGb = dedicated.HasValue || reserved.HasValue
                            ? (dedicated.GetValueOrDefault() + (double)reserved.GetValueOrDefault()) / 1073741824d : null,
                        SharedMemoryGb = ReadUInt64(adapter, AdapterProperty.SharedSystemMemory) / 1073741824d,
                        DriverVersion = version is > 0
                            ? $"{version.Value >> 48}.{(version.Value >> 32) & 0xffff}.{(version.Value >> 16) & 0xffff}.{version.Value & 0xffff}" : null
                    });
                }
                finally
                {
                    if (adapter is not null) Marshal.ReleaseComObject(adapter);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            // Older Windows versions or a driver change may leave DXCore unavailable.
        }
        finally
        {
            if (list is not null) Marshal.ReleaseComObject(list);
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
        return identities;
    }

    private static byte[] ReadProperty(IAdapter adapter, AdapterProperty property)
    {
        if (!adapter.IsPropertySupported(property) || adapter.GetPropertySize(property, out var size) < 0 ||
            size == 0 || size > 65536)
            return [];

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (adapter.GetProperty(property, size, buffer) < 0) return [];
            var bytes = new byte[(int)size];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ulong? ReadUInt64(IAdapter adapter, AdapterProperty property)
    {
        var data = ReadProperty(adapter, property);
        return data.Length == sizeof(ulong) ? BinaryPrimitives.ReadUInt64LittleEndian(data) : null;
    }

    private enum AdapterProperty : uint
    {
        InstanceLuid = 0,
        DriverVersion = 1,
        DriverDescription = 2,
        DedicatedAdapterMemory = 7,
        DedicatedSystemMemory = 8,
        SharedSystemMemory = 9,
        IsHardware = 11,
        IsIntegrated = 12
    }

    [DllImport("dxcore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DXCoreCreateAdapterFactory(in Guid iid, out IAdapterFactory? factory);

    // These interfaces retain the native method order through the last slot we call.
    [ComImport, Guid("78ee5945-c36e-4b13-a669-005dd11c0f06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapterFactory
    {
        [PreserveSig] int CreateAdapterList(uint attributeCount, in Guid attribute, in Guid iid, out IAdapterList? list);
    }

    [ComImport, Guid("526c7776-40e9-459b-b711-f32ad76dfc28"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapterList
    {
        [PreserveSig] int GetAdapter(uint index, in Guid iid, out IAdapter? adapter);
        [PreserveSig] uint GetAdapterCount();
    }

    [ComImport, Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAdapter
    {
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)] bool IsValid();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)] bool IsAttributeSupported(in Guid attribute);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.I1)] bool IsPropertySupported(AdapterProperty property);
        [PreserveSig] int GetProperty(AdapterProperty property, nuint size, nint buffer);
        [PreserveSig] int GetPropertySize(AdapterProperty property, out nuint size);
    }
}
