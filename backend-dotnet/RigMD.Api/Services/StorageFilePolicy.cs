using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Api.Services;

public sealed record StorageFileTarget(string CategoryId, string Root, string RelativeName, long Bytes, long LastWrite, string FileIdentity);

public static class StorageFilePolicy
{
    public static string? RootFor(string category)
    {
        var root = category switch
        {
        "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), _ => null
        };
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || Path.GetFullPath(root).TrimEnd('\\') == Path.GetPathRoot(root)?.TrimEnd('\\')) return null;
        foreach (var special in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.Startup })
        {
            var protectedRoot = Environment.GetFolderPath(special).TrimEnd('\\');
            if (protectedRoot.Length > 0 && (root.Equals(protectedRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(protectedRoot + "\\", StringComparison.OrdinalIgnoreCase))) return null;
        }
        return root;
    }

    // Personal content only. Application source, executable code, credentials and project dependency folders stay read-only.
    public static bool EligibleName(string relative) => relative.Length is > 0 and < 240 && !Path.IsPathRooted(relative) &&
        !relative.Any(char.IsControl) && !relative.Contains(':') && !relative.Contains('/') &&
        relative.Split('\\').All(part => part.Length > 0 && part is not "." and not ".." && !part.EndsWith(' ') && !part.EndsWith('.') &&
            !Regex.IsMatch(part, @"\A(?:\.git|\.svn|node_modules|packages|bin|obj|\.vs|\.vscode|appdata|windows|program files|startup)\z", RegexOptions.IgnoreCase)) &&
        Regex.IsMatch(Path.GetExtension(relative), @"\A\.(?:txt|pdf|docx?|xlsx?|pptx?|odt|ods|rtf|csv|jpe?g|png|gif|webp|bmp|tiff?|heic|mp[34]|m4[av]|mov|mkv|avi|wav|flac|ogg|zip|7z|rar)\z", RegexOptions.IgnoreCase);

    public static string Resolve(string root, string relative)
    {
        if (!EligibleName(relative) || !Path.IsPathFullyQualified(root)) throw new InvalidOperationException("This file is not eligible for personal-file cleanup.");
        var fullRoot = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(path, @"\A[A-Za-z]:\\") || path[2..].Contains(':'))
            throw new InvalidOperationException("The selected file is outside its personal folder.");
        return path;
    }

    public static StorageFileTarget Inspect(string category, string root, string relative)
    {
        var path = Resolve(root, relative);
        using var pins = PinParents(path);
        using var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (StorageOverviewReader.MustSkip((FileAttributes)info.Attributes) || (info.Attributes & (uint)(FileAttributes.Directory | FileAttributes.System | FileAttributes.ReadOnly)) != 0 || info.Links != 1)
            throw new InvalidOperationException("Linked, protected, read-only or cloud files are not eligible.");
        return new(category, root, relative, ((long)info.SizeHigh << 32) | info.SizeLow,
            ((long)info.WriteHigh << 32) | info.WriteLow, $"{info.Serial:X8}:{info.IndexHigh:X8}:{info.IndexLow:X8}");
    }

    public static IDisposable PinParents(string path)
    {
        var pins = new Pins();
        try
        {
            var parents = new Stack<DirectoryInfo>();
            for (var parent = new FileInfo(path).Directory; parent != null; parent = parent.Parent) parents.Push(parent);
            foreach (var parent in parents)
            {
                var handle = CreateFile(parent.FullName, 0x80, 3, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                pins.Handles.Add(handle);
                if (StorageOverviewReader.MustSkip(parent.Attributes)) throw new InvalidOperationException("Linked/cloud folders cannot be changed here.");
            }
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }
    private sealed class Pins : IDisposable
    {
        public List<SafeFileHandle> Handles { get; } = [];
        public void Dispose() { foreach (var handle in Handles.AsEnumerable().Reverse()) handle.Dispose(); Handles.Clear(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoData { public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Serial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoData information);
}
