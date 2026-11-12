using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace RigMD.Api.Services;

public sealed class VerifiedUninstallExecutable : IDisposable
{
    private readonly List<IDisposable> _locks = [];
    public string Sha256 { get; private set; } = "";
    public string Signer { get; private set; } = "";

    public static VerifiedUninstallExecutable Open(string path)
    {
        var result = new VerifiedUninstallExecutable();
        try
        {
            if (!Path.IsPathFullyQualified(path) || !System.Text.RegularExpressions.Regex.IsMatch(path, @"\A[A-Za-z]:\\") || path[2..].Contains(':') ||
                new DriveInfo(Path.GetPathRoot(path)!).DriveType != DriveType.Fixed)
                throw new InvalidOperationException("A local fixed-drive executable is required.");
            var parents = new Stack<DirectoryInfo>();
            for (var parent = new FileInfo(path).Directory; parent != null; parent = parent.Parent) parents.Push(parent);
            foreach (var parent in parents)
            {
                var handle = CreateFile(parent.FullName, 0x80, 1, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
                result._locks.Add(handle);
                if (StorageOverviewReader.MustSkip(parent.Attributes)) throw new InvalidOperationException("Linked or cloud executable locations are not supported.");
            }
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            result._locks.Add(stream);
            if (stream.Length is <= 0 or > 536870912 || StorageOverviewReader.MustSkip(File.GetAttributes(path)))
                throw new InvalidOperationException("Unsupported executable size or file attributes.");
            result.Signer = VerifiedSigner(path, stream.SafeFileHandle);
            stream.Position = 0;
            result.Sha256 = Convert.ToHexString(SHA256.HashData(stream));
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public static (string Sha256, string Signer) Inspect(string path)
    {
        using var verified = Open(path);
        return (verified.Sha256, verified.Signer);
    }

    private static string VerifiedSigner(string path, SafeFileHandle handle)
    {
        var info = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path, Handle = handle.DangerousGetHandle() };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(info, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 1, UnionChoice = 1,
            File = pointer, StateAction = 1, ProviderFlags = 0x1000 | 0x80 | 0x2000 };
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) throw new InvalidOperationException("Windows could not verify the uninstaller signature.");
            var provider = WTHelperProvDataFromStateData(data.State);
            var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            var signed = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (signed == IntPtr.Zero || Marshal.ReadInt32(signed) < IntPtr.Size * 2) throw new InvalidOperationException("Verified signer unavailable.");
            var certificatePointer = Marshal.ReadIntPtr(signed, IntPtr.Size);
            if (certificatePointer == IntPtr.Zero) throw new InvalidOperationException("Verified signer unavailable.");
            var context = Marshal.PtrToStructure<CertificateContext>(certificatePointer);
            if (context.Encoded == IntPtr.Zero || context.EncodedSize is 0 or > 65536) throw new InvalidOperationException("Invalid signing certificate.");
            var bytes = new byte[context.EncodedSize]; Marshal.Copy(context.Encoded, bytes, 0, bytes.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(bytes);
            return certificate.GetNameInfo(X509NameType.SimpleName, false);
        }
        finally
        {
            data.StateAction = 2; _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }

    public void Dispose() { for (var index = _locks.Count - 1; index >= 0; index--) _locks[index].Dispose(); _locks.Clear(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint UiChoice, RevocationChecks, UnionChoice; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint ProviderFlags, UiContext; public IntPtr SignatureSettings; }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext { public uint Encoding; public IntPtr Encoded; public uint EncodedSize; public IntPtr Info, Store; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint index, bool counterSigner, uint counterIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint index);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
}
