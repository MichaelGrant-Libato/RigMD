using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace RigMD.Infrastructure.Persistence;

internal static class LocalDiagnosticDeviceIdentity
{
    public static string Read()
    {
        if (!OperatingSystem.IsWindows()) return string.Empty;
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var machineId = key?.GetValue("MachineGuid") as string;
            return Guid.TryParse(machineId, out var id) && id != Guid.Empty
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("RigMD:" + id.ToString("D"))))
                : string.Empty;
        }
        catch { return string.Empty; }
    }
}
