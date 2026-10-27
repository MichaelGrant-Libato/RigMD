using System.Security.Cryptography;
using System.Text.Json;

namespace RigMD.Infrastructure.Ai;

public sealed record SavedCloudAiAccount(string Project, Guid UserId, string RefreshToken);
public interface ICloudAiCredentialStore
{
    SavedCloudAiAccount? Read();
    void Write(SavedCloudAiAccount account);
    void Clear();
}

// Store only the refresh credential, protected for this Windows user, outside the installer and database.
public sealed class WindowsCloudAiCredentialStore(string path) : ICloudAiCredentialStore
{
    private static readonly byte[] Entropy = "RigMD.DesktopAccount.v1"u8.ToArray();
    public SavedCloudAiAccount? Read()
    {
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 32768) { Clear(); return null; }
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<SavedCloudAiAccount>(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        { try { Clear(); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { } return null; }
    }
    public void Write(SavedCloudAiAccount account)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(account);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public void Clear() { if (File.Exists(path)) File.Delete(path); }
}
