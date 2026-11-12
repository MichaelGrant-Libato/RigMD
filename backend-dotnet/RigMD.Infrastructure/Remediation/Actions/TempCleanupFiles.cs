using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Infrastructure.Remediation.Actions;

internal sealed record TempFileIdentity(uint Volume, uint IndexHigh, uint IndexLow, long Bytes, long Created, long Written);
internal sealed record TempCleanupFile(string Root, string Path, TempFileIdentity Identity);
internal sealed record TempCleanupRoot(string Path, TempFileIdentity Identity);

// File identity is checked through the same handle used to mark deletion. No file contents are read.
internal static class TempCleanupFiles
{
    internal static bool Excluded(uint attributes) => (attributes & ((uint)(FileAttributes.ReparsePoint | FileAttributes.Offline) | 0x440000)) != 0;
    internal static bool ExcludedFile(Info info) => Excluded(info.Attributes) || info.Links != 1 ||
        (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly)) != 0;
    internal static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !System.Text.RegularExpressions.Regex.IsMatch(path, @"\A[A-Za-z]:\\") || path[2..].Contains(':'))
            throw new InvalidOperationException("Cleanup requires an absolute local folder.");
        var root = Path.GetFullPath(path).TrimEnd('\\');
        if (root.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
            throw new InvalidOperationException("Drive roots and non-fixed drives cannot be cleaned.");
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var protectedRoot = Environment.GetFolderPath(folder).TrimEnd('\\');
            if (protectedRoot.Length > 0 && root.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("This protected root cannot be cleaned.");
        }
        return root;
    }
    internal static SafeFileHandle OpenFile(string path, bool delete)
    {
        var handle = CreateFile(path, 0x80u | (delete ? 0x10000u : 0), delete ? 1u : 7u, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        return handle;
    }
    internal static Info Read(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }
    internal static TempFileIdentity Identity(Info info) => new(info.Volume, info.IndexHigh, info.IndexLow,
        ((long)info.SizeHigh << 32) | info.SizeLow, ((long)info.CreatedHigh << 32) | info.CreatedLow, ((long)info.WriteHigh << 32) | info.WriteLow);
    internal static void MarkDeleted(SafeFileHandle handle)
    {
        var disposition = new Disposition { Delete = true };
        if (!SetFileInformationByHandle(handle, 4, ref disposition, (uint)Marshal.SizeOf<Disposition>())) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static TempFileIdentity DirectoryIdentity(string path, Pins pins)
    {
        var handle = CreateFile(path, 0x80, 3, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
        pins.Handles.Add(handle);
        var info = Read(handle);
        if (Excluded(info.Attributes) || (info.Attributes & (uint)FileAttributes.Directory) == 0)
            throw new InvalidOperationException("Linked, offline or cloud-recall cleanup folders are not supported.");
        return Identity(info);
    }
    internal static Pins PinParents(string directory)
    {
        var pins = new Pins();
        try
        {
            var pending = new Stack<string>();
            for (var item = new DirectoryInfo(directory); item != null; item = item.Parent) pending.Push(item.FullName);
            while (pending.TryPop(out var item)) DirectoryIdentity(item, pins);
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }
    internal sealed class Pins : IDisposable
    {
        internal readonly List<SafeFileHandle> Handles = [];
        public void Dispose() { foreach (var handle in Handles.AsEnumerable().Reverse()) handle.Dispose(); Handles.Clear(); }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Info
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, ref Disposition info, uint size);
}
