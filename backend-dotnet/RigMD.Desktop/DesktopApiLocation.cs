using System;
using System.IO;

namespace RigMD.Desktop;

internal static class DesktopApiLocation
{
    internal static string Resolve(string desktopDirectory)
    {
        var output = new DirectoryInfo(Path.GetFullPath(desktopDirectory));
        var installedExe = Path.GetFullPath(Path.Combine(output.FullName, "..", "Api", "RigMD.Api.exe"));
        if (File.Exists(installedExe))
            return installedExe;

        var configuration = output.Parent;
        var bin = configuration?.Parent;
        var project = bin?.Parent;
        if (project is not null && configuration is not null
            && string.Equals(bin?.Name, "bin", StringComparison.OrdinalIgnoreCase)
            && string.Equals(project.Name, "RigMD.Desktop", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(project.FullName, "RigMD.Desktop.csproj")))
        {
            var localExe = Path.GetFullPath(Path.Combine(project.FullName, "..", "RigMD.Api",
                "bin", configuration.Name, output.Name, "RigMD.Api.exe"));
            if (File.Exists(localExe))
                return localExe;
        }

        throw new FileNotFoundException(
            "The local API executable was not found. For a source build, build RigMD.Desktop.csproj "
            + "with the same configuration before launching. For an installed copy, reinstall RigMD.",
            installedExe);
    }
}
