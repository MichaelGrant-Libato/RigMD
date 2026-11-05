using RigMD.Desktop;

namespace RigMD.Tests.Desktop;

public sealed class DesktopApiLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "RigMD-ApiLocation-" + Guid.NewGuid());

    [Fact]
    public void ResolvesInstalledSiblingApi()
    {
        var desktop = Path.Combine(_root, "Desktop");
        var api = CreateFile("Api", "RigMD.Api.exe");
        Assert.Equal(api, DesktopApiLocation.Resolve(desktop));
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void ResolvesMatchingSourceBuild(string configuration)
    {
        CreateFile("RigMD.Desktop", "RigMD.Desktop.csproj");
        var desktop = Path.Combine(_root, "RigMD.Desktop", "bin", configuration, "net10.0-windows");
        var api = CreateFile("RigMD.Api", "bin", configuration, "net10.0-windows", "RigMD.Api.exe");
        Assert.Equal(api, DesktopApiLocation.Resolve(desktop));
    }

    [Fact]
    public void PrefersInstalledApiWhenBothLocationsExist()
    {
        CreateFile("RigMD.Desktop", "RigMD.Desktop.csproj");
        CreateFile("RigMD.Api", "bin", "Release", "net10.0-windows", "RigMD.Api.exe");
        var api = CreateFile("RigMD.Desktop", "bin", "Release", "Api", "RigMD.Api.exe");
        var desktop = Path.Combine(_root, "RigMD.Desktop", "bin", "Release", "net10.0-windows");
        Assert.Equal(api, DesktopApiLocation.Resolve(desktop));
    }

    [Fact]
    public void RejectsSourceLayoutWithoutProjectMarker()
    {
        CreateFile("RigMD.Api", "bin", "Release", "net10.0-windows", "RigMD.Api.exe");
        var desktop = Path.Combine(_root, "RigMD.Desktop", "bin", "Release", "net10.0-windows");
        Assert.Throws<FileNotFoundException>(() => DesktopApiLocation.Resolve(desktop));
    }

    [Fact]
    public void DoesNotUseAnotherBuildConfiguration()
    {
        CreateFile("RigMD.Desktop", "RigMD.Desktop.csproj");
        CreateFile("RigMD.Api", "bin", "Debug", "net10.0-windows", "RigMD.Api.exe");
        var desktop = Path.Combine(_root, "RigMD.Desktop", "bin", "Release", "net10.0-windows");
        Assert.Throws<FileNotFoundException>(() => DesktopApiLocation.Resolve(desktop));
    }

    [Fact]
    public void MissingApiExplainsHowToRecover()
    {
        var error = Assert.Throws<FileNotFoundException>(() => DesktopApiLocation.Resolve(Path.Combine(_root, "Desktop")));
        Assert.Contains("build RigMD.Desktop.csproj", error.Message);
        Assert.Contains("reinstall RigMD", error.Message);
    }

    private string CreateFile(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    public void Dispose()
    {
        var absoluteRoot = Path.GetFullPath(_root);
        var prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "RigMD-ApiLocation-");
        if (absoluteRoot.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(absoluteRoot))
            Directory.Delete(absoluteRoot, recursive: true);
    }
}
