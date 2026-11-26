using System.Management;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Windows;

public class WmiStorageProvider : IStorageProvider
{
    private string _primaryStorageType = "Unknown";
    private readonly List<StorageDriveDto> _storageDrives = new();

    public string GetPrimaryStorageType()
    {
        lock (_storageDrives)
        {
        // Must be called after GetStorageDrives, or we can just run it
        if (!_storageDrives.Any())
            GetStorageDrives();
            
        return _primaryStorageType;
        }
    }

    public List<StorageDriveDto> GetStorageDrives()
    {
        lock (_storageDrives)
        {
            return ReadStorageDrives();
        }
    }

    private List<StorageDriveDto> ReadStorageDrives()
    {
        // Refresh status on every scan; only immutable model metadata would be safe to cache.
        _storageDrives.Clear();
        _primaryStorageType = "Unknown";

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType, InterfaceType, Size, Index, Status FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString() ?? "Unknown";
                var mediaType = obj["MediaType"]?.ToString() ?? "Unknown";
                var interfaceType = obj["InterfaceType"]?.ToString() ?? "Unknown";
                var status = obj["Status"]?.ToString() ?? "Unknown";
                
                var sizeGb = 0.0;
                if (obj["Size"] != null && long.TryParse(obj["Size"].ToString(), out var sizeBytes))
                {
                    sizeGb = Math.Round(sizeBytes / (1024.0 * 1024.0 * 1024.0), 2);
                }
                
                int? diskIndex = null;
                if (obj["Index"] != null && int.TryParse(obj["Index"].ToString(), out var idx))
                {
                    diskIndex = idx;
                }
                
                var (type, source) = ClassifyStorage(model, mediaType, interfaceType);
                
                _storageDrives.Add(new StorageDriveDto
                {
                    Model = model,
                    MediaType = mediaType,
                    Interface = interfaceType,
                    SizeGb = sizeGb,
                    DiskIndex = diskIndex,
                    Type = type,
                    DetectionSource = source,
                    IsFailingSmart = ReportsHealthWarning(status)
                });
            }
            ReadModernStorageMetadata();

            // Determine primary type
            var types = _storageDrives.Select(d => d.Type).ToList();
            if (types.Contains("NVMe SSD")) _primaryStorageType = "NVMe SSD";
            else if (types.Contains("SATA SSD")) _primaryStorageType = "SATA SSD";
            else if (types.Contains("SSD")) _primaryStorageType = "SSD";
            else if (types.Contains("HDD")) _primaryStorageType = "HDD";
            else if (types.Any()) _primaryStorageType = types.First();

            _storageDrives.Sort((a, b) => (a.DiskIndex ?? 999).CompareTo(b.DiskIndex ?? 999));
        }
        catch
        {
            // Ignore
        }

        return _storageDrives.ToList();
    }

    private void ReadModernStorageMetadata()
    {
        const string storageNamespace = @"root\Microsoft\Windows\Storage";
        try
        {
            var devices = new List<(string Name, string Serial, ulong Size, int Media)>();
            try
            {
                using var mediaQuery = new ManagementObjectSearcher(storageNamespace,
                    "SELECT FriendlyName, SerialNumber, Size, MediaType FROM MSFT_PhysicalDisk");
                using var media = mediaQuery.Get();
                foreach (ManagementObject row in media)
                using (row)
                    if (ulong.TryParse(row["Size"]?.ToString(), out var size) &&
                        int.TryParse(row["MediaType"]?.ToString(), out var kind))
                        devices.Add((row["FriendlyName"]?.ToString()?.Trim() ?? "",
                            row["SerialNumber"]?.ToString()?.Trim() ?? "", size, kind));
            }
            catch (ManagementException) { }

            using var diskQuery = new ManagementObjectSearcher(storageNamespace,
                "SELECT Number, BusType, FriendlyName, SerialNumber, Size FROM MSFT_Disk");
            using var disks = diskQuery.Get();
            foreach (ManagementObject row in disks)
            using (row)
            {
                if (!int.TryParse(row["Number"]?.ToString(), out var number) ||
                    _storageDrives.FirstOrDefault(drive => drive.DiskIndex == number) is not { } drive) continue;

                var bus = row["BusType"]?.ToString() switch
                {
                    "1" => "SCSI", "6" => "Fibre Channel", "7" => "USB", "8" => "RAID", "9" => "iSCSI",
                    "10" => "SAS", "11" => "SATA", "12" => "SD", "13" => "MMC", "14" => "Virtual",
                    "15" => "File-backed virtual", "16" => "Storage Spaces", "17" => "NVMe", "19" => "UFS",
                    _ => null
                };
                drive.BusType = bus;
                if (bus is not null)
                {
                    drive.Interface = bus;
                    drive.DetectionSource = "Windows storage bus";
                }

                if (!ulong.TryParse(row["Size"]?.ToString(), out var size)) continue;
                var serial = row["SerialNumber"]?.ToString()?.Trim();
                var name = row["FriendlyName"]?.ToString()?.Trim();
                var matches = devices.Where(item => item.Size == size && !string.IsNullOrWhiteSpace(serial) &&
                    string.Equals(item.Serial, serial, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (matches.Length == 0 && !string.IsNullOrWhiteSpace(name))
                    matches = devices.Where(item => item.Size == size &&
                        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                var mediaType = matches.Length == 1 ? matches[0].Media switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => null } : null;
                if (mediaType is not null)
                {
                    drive.MediaType = mediaType;
                    drive.Type = mediaType == "SSD" && bus is ("SATA" or "NVMe") ? $"{bus} SSD" : mediaType;
                    drive.DetectionSource = "Windows storage bus and media type";
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
    }

    public List<DiskVolumeDto> GetAllDisks()
    {
        var volumes = new List<DiskVolumeDto>();
        try
        {
            var partitionMap = GetLogicalToPhysicalMapping();

            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed);
            foreach (var d in drives)
            {
                var totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                var freeGb = d.TotalFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var usedGb = totalGb - freeGb;
                var percent = totalGb > 0 ? (usedGb / totalGb) * 100 : 0;
                
                var driveLetter = d.Name.TrimEnd('\\', '/');
                int? diskIndex = partitionMap.ContainsKey(driveLetter) ? partitionMap[driveLetter] : null;

                volumes.Add(new DiskVolumeDto
                {
                    Drive = d.Name,
                    Mountpoint = d.Name,
                    FsType = d.DriveFormat,
                    DiskIndex = diskIndex,
                    TotalGb = Math.Round(totalGb, 2),
                    UsedGb = Math.Round(usedGb, 2),
                    UsagePercent = Math.Round(percent, 1)
                });
            }
        }
        catch
        {
            // Ignore
        }
        return volumes;
    }

    internal static bool ReportsHealthWarning(string? status) =>
        status?.Trim().ToLowerInvariant() is "pred fail" or "error" or "degraded";

    private Dictionary<string, int> GetLogicalToPhysicalMapping()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition");
            foreach (var obj in searcher.Get())
            {
                var antecedent = obj["Antecedent"]?.ToString() ?? "";
                var dependent = obj["Dependent"]?.ToString() ?? "";

                var driveMatch = System.Text.RegularExpressions.Regex.Match(dependent, "DeviceID=\"([A-Za-z]:)\"");
                var diskMatch = System.Text.RegularExpressions.Regex.Match(antecedent, "Disk #(\\d+)");

                if (driveMatch.Success && diskMatch.Success)
                {
                    var drive = driveMatch.Groups[1].Value;
                    if (int.TryParse(diskMatch.Groups[1].Value, out var index))
                    {
                        map[drive] = index;
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }
        return map;
    }

    private (string type, string source) ClassifyStorage(string model, string mediaType, string interfaceType)
    {
        var m = model.ToLowerInvariant();
        var media = mediaType.ToLowerInvariant();
        var intf = interfaceType.ToLowerInvariant();

        if (intf.Contains("nvme") || m.Contains("nvme") || m.Contains("pcie") || m.Contains("980 pro") || m.Contains("970 evo") || m.Contains("sn850") || m.Contains("sn770") || m.Contains("sn570") || m.Contains("sn580"))
            return ("NVMe SSD", "Model/Interface Hint");
            
        if (m.Contains("ssd") || media.Contains("solid state") || m.Contains("wds") || m.Contains("wdc wds") || m.Contains("kingston") || m.Contains("crucial") || m.Contains("bx500") || m.Contains("mx500") || m.Contains("sandisk"))
        {
            if (intf.Contains("ide") || intf.Contains("sata") || intf.Contains("ata") || m.Contains("wds240") || m.Contains("green") || m.Contains("blue"))
                return ("SATA SSD", "Media/Interface Hint");
            return ("SSD", "Media Hint");
        }
        
        if (m.Contains("hdd") || m.Contains("wd blue") || m.Contains("barracuda") || (m.Length > 2 && m.StartsWith("st") && char.IsDigit(m[2])) || m.Contains("wd10") || m.Contains("wd20") || m.Contains("wd40"))
            return ("HDD", "Model Hint");

        return ("Unknown", "Fallback");
    }
}
