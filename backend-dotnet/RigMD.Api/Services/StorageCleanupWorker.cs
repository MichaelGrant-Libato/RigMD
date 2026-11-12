using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;

namespace RigMD.Api.Services;

public sealed record RecycleBinReading(long Bytes, long Items);
public sealed record StorageCleanupTarget(string Kind, string Drive, string UserSid, StorageFileTarget[] Files, RecycleBinReading Bin);

public static class StorageCleanupWorker
{
    public const string Switch = "--storage-cleanup-helper";
    public const string RequestVariable = "RIGMD_STORAGE_CLEANUP_REQUEST";

    public static RecycleBinReading ReadBin(string drive)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(drive, @"\A[A-Za-z]:\\\z")) throw new InvalidOperationException("Invalid fixed drive.");
        RecycleBinReading reading;
        if (IntPtr.Size == 8)
        {
            var info = new BinInfo64 { Size = (uint)Marshal.SizeOf<BinInfo64>() };
            Marshal.ThrowExceptionForHR(SHQueryRecycleBin64(drive, ref info));
            reading = new(info.Bytes, info.Items);
        }
        else
        {
            var info = new BinInfo32 { Size = (uint)Marshal.SizeOf<BinInfo32>() };
            Marshal.ThrowExceptionForHR(SHQueryRecycleBin32(drive, ref info));
            reading = new(info.Bytes, info.Items);
        }
        if (reading.Bytes < 0 || reading.Items < 0) throw new InvalidOperationException("Recycle Bin reading is unavailable.");
        return reading;
    }

    public static int RunFromEnvironment()
    {
        var json = Environment.GetEnvironmentVariable(RequestVariable);
        Environment.SetEnvironmentVariable(RequestVariable, null);
        if (json == null || json.Length > 24000) return 87;
        var result = 13;
        var thread = new Thread(() =>
        {
            var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0;
            if (!initialized) return;
            try
            {
                var target = JsonSerializer.Deserialize<StorageCleanupTarget>(json);
                using var identity = WindowsIdentity.GetCurrent();
                if (target == null || identity.User?.Value != target.UserSid || new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) || NativeUninstallRunner.DesktopUser() != target.UserSid)
                    return;
                if (new DriveInfo(target.Drive).DriveType != DriveType.Fixed) return;
                if (target.Kind == "empty_recycle_bin" && target.Files.Length == 0)
                {
                    if (ReadBin(target.Drive) != target.Bin || target.Bin.Items == 0) return;
                    // Current account and exact selected drive only. Separate irreversible approval was consumed by the API.
                    result = SHEmptyRecycleBin(IntPtr.Zero, target.Drive, 7) == 0 && ReadBin(target.Drive).Items == 0 ? 0 : 13;
                    return;
                }
                if (target.Kind != "recycle_files" || target.Files.Length is < 1 or > 20) return;
                foreach (var file in target.Files)
                {
                    if (StorageFilePolicy.RootFor(file.CategoryId) != file.Root || !StorageOverviewReader.OnDrive(file.Root, target.Drive) ||
                        StorageFilePolicy.Inspect(file.CategoryId, file.Root, file.RelativeName) != file) return;
                }
                foreach (var file in target.Files) WindowsRecycleOperation.Recycle(file);
                result = 0;
            }
            catch (OperationCanceledException) { result = 1223; }
            catch (Exception) { result = 13; }
            finally { CoUninitialize(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        return result;
    }
    // shellapi.h packs this structure to 1 only on x86; x64 uses normal 8-byte alignment.
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct BinInfo64 { public uint Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BinInfo32 { public uint Size; public long Bytes, Items; }
    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHQueryRecycleBin64(string root, ref BinInfo64 info);
    [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHQueryRecycleBin32(string root, ref BinInfo32 info);
    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHEmptyRecycleBin(IntPtr window, string root, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)] private static extern void CoUninitialize();
}
