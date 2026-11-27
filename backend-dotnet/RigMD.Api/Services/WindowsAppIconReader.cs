using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace RigMD.Api.Services;

internal sealed class WindowsAppIconReader(IEnumerable<string> localVolumeRoots)
{
    private const int MaxIconBytes = 16 * 1024;
    private readonly HashSet<string> _localRoots = new(localVolumeRoots, StringComparer.OrdinalIgnoreCase);

    internal string? ReadDesktopIcon(string? displayIcon)
    {
        if (ParseDesktopIcon(displayIcon) is not { } reference) return null;
        try
        {
            if (!IsOrdinaryLocalPath(reference.Path)
                || new FileInfo(reference.Path).Length > 512L * 1024 * 1024) return null;
            // Extract the icon resource. No file or registered command is executed.
            using var icon = Icon.ExtractIcon(reference.Path, reference.Index, 32);
            if (icon is null) return null;
            using var image = icon.ToBitmap();
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            return png.Length <= MaxIconBytes ? Convert.ToBase64String(png.ToArray()) : null;
        }
        catch (Exception ex) when (IsIconReadFailure(ex)) { return null; }
    }

    internal string? ReadPackageIcon(string? installPath)
    {
        if (installPath is null) return null;
        try
        {
            var manifestPath = Path.Combine(installPath, "AppxManifest.xml");
            if (!IsOrdinaryLocalPath(manifestPath)) return null;
            using var manifestFile = File.OpenRead(manifestPath);
            using var reader = XmlReader.Create(manifestFile, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 512 * 1024
            });
            var manifest = XDocument.Load(reader);
            var visualLogo = manifest.Descendants().FirstOrDefault(element => element.Name.LocalName == "VisualElements")
                ?.Attribute("Square44x44Logo")?.Value;
            var packageLogo = manifest.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "Properties")
                ?.Elements().FirstOrDefault(element => element.Name.LocalName == "Logo")?.Value;
            return ReadPackageLogo(installPath, visualLogo) ?? ReadPackageLogo(installPath, packageLogo);
        }
        catch (Exception ex) when (IsIconReadFailure(ex) || ex is XmlException) { return null; }
    }

    internal static (string Path, int Index)? ParseDesktopIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var pathText = displayIcon.Trim();
        var index = 0;
        var comma = pathText.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(pathText.AsSpan(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            if (parsed == int.MinValue) return null;
            index = parsed;
            pathText = pathText[..comma];
        }
        if (NormalizeInstallPath(pathText) is not { } path) return null;
        return Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".ico" ? (path, index) : null;
    }

    internal static string? NormalizeInstallPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32767) return null;
        var path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).Replace('/', '\\');
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\'
            || path.AsSpan(2).Contains(':') || path.IndexOfAny(['"', '<', '>', '*', '?', '|']) >= 0
            || path.Any(char.IsControl))
            return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private string? ReadPackageLogo(string installPath, string? relativeLogo)
    {
        if (string.IsNullOrWhiteSpace(relativeLogo) || Path.IsPathRooted(relativeLogo) || relativeLogo.Contains(':')) return null;
        var fullPath = Path.GetFullPath(Path.Combine(installPath, relativeLogo));
        var packageRoot = Path.TrimEndingDirectorySeparator(installPath) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(fullPath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            || fullPath.AsSpan(2).Contains(':')) return null;

        var stem = Path.Combine(Path.GetDirectoryName(fullPath)!, Path.GetFileNameWithoutExtension(fullPath));
        foreach (var candidate in new[]
        {
            fullPath, stem + ".targetsize-32_altform-unplated.png", stem + ".targetsize-32.png",
            stem + ".scale-100.png", stem + ".scale-125.png", stem + ".scale-150.png", stem + ".scale-200.png"
        })
        {
            try
            {
                if (!IsOrdinaryLocalPath(candidate)) continue;
                using var stream = File.OpenRead(candidate);
                if (stream.Length is < 24 or > MaxIconBytes) continue;
                var bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
                if (IsSmallPng(bytes)) return Convert.ToBase64String(bytes);
            }
            catch (Exception ex) when (IsIconReadFailure(ex)) { }
        }
        return null;
    }

    private bool IsOrdinaryLocalPath(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is null || !_localRoots.Contains(root)) return false;
        var current = root;
        foreach (var part in path[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    private static bool IsSmallPng(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        && bytes[12..16].SequenceEqual("IHDR"u8)
        && BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]) is > 0 and <= 128
        && BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]) is > 0 and <= 128;

    private static bool IsIconReadFailure(Exception ex) =>
        ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException or SecurityException or ExternalException;
}
