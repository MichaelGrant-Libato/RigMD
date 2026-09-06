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
        // Must be called after GetStorageDrives, or we can just run it
        if (!_storageDrives.Any())
            GetStorageDrives();
            
        return _primaryStorageType;
    }

    public List<StorageDriveDto> GetStorageDrives()
    {
        if (_storageDrives.Any()) return _storageDrives;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType, InterfaceType, Size, Index, Status FROM Win32_DiskDrive");
            foreach (var obj in searcher.Get())
            {
                var model = obj["Model"]?.ToString() ?? "Unknown";
                var mediaType = obj["MediaType"]?.ToString() ?? "Unknown";
                var interfaceType = obj["InterfaceType"]?.ToString() ?? "Unknown";
                var status = obj["Status"]?.ToString() ?? "OK";
                
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
                    IsFailingSmart = !string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase)
                });
            }

            // Determine primary type
            var types = _storageDrives.Select(d => d.Type).ToList();
            if (types.Contains("NVMe SSD")) _primaryStorageType = "NVMe SSD";
            else if (types.Contains("SATA SSD")) _primaryStorageType = "SATA SSD";
            else if (types.Contains("SSD")) _primaryStorageType = "SSD";
            else if (types.Contains("HDD")) _primaryStorageType = "HDD";
            else if (types.Any()) _primaryStorageType = types.First();
        }
        catch
        {
            // Ignore
        }

        return _storageDrives;
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

        if (intf.Contains("nvme") || m.Contains("nvme") || m.Contains("980 pro") || m.Contains("970 evo"))
            return ("NVMe SSD", "Model/Interface Hint");
            
        if (m.Contains("ssd") || media.Contains("solid state"))
        {
            if (intf.Contains("ide") || intf.Contains("sata") || intf.Contains("ata"))
                return ("SATA SSD", "Media/Interface Hint");
            return ("SSD", "Media Hint");
        }
        
        if (m.Contains("hdd") || m.Contains("wd blue") || m.Contains("barracuda") || (m.Length > 2 && m.StartsWith("st") && char.IsDigit(m[2])))
            return ("HDD", "Model Hint");

        return ("Unknown", "Fallback");
    }
}
