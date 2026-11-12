using System.Management;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Infrastructure.Windows;

public sealed class WindowsStorageBootIdentity : IStorageBootIdentity
{
    public string? Read()
    {
        try
        {
            using var query = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem");
            query.Options.Timeout = TimeSpan.FromSeconds(3);
            using var rows = query.Get();
            foreach (ManagementBaseObject row in rows)
                using (row)
                    if (row["LastBootUpTime"] is string value && !value.Contains('*'))
                        return ManagementDateTimeConverter.ToDateTime(value).ToUniversalTime().ToString("O");
        }
        catch { /* An unknown boot cannot release an interrupted-operation safety lock. */ }
        return null;
    }
}
