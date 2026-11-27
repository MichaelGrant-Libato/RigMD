using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public class InstalledAppInventoryTests
{
    [Fact]
    public void DesktopInventoryReadsRegisteredMetadataWithoutReadingRemovalCommands()
    {
        var values = new Dictionary<string, string>
        {
            ["Comments"] = "  Example app\r\nfor editing.  ",
            ["InstallLocation"] = @"D:\Apps\Example",
            ["InstallDate"] = "20241127"
        };
        var registration = new StorageAppEntry("Example", "Publisher", "1.2", 204800, "Machine");
        var app = InstalledAppInventory.ReadDesktopMetadata(registration, "test", key => values[key], machine: true);

        Assert.Equal("Example app  for editing.", app.Description);
        Assert.Equal(@"D:\Apps\Example", app.InstallPath);
        Assert.Equal("D:", app.Drive);
        Assert.Equal("2024-11-27", app.RegisteredDate);
        Assert.Equal(204800, app.EstimatedBytes);
    }

    [Fact]
    public void MissingMetadataStaysMissingAndDescriptionCanUseTheAlternateRegistryValue()
    {
        var registration = new StorageAppEntry("Example", "", "", null, "Current account");
        var app = InstalledAppInventory.ReadDesktopMetadata(registration, "test", key => key switch
        {
            "DisplayDescription" => "Publisher description",
            "InstallDate" => "20241199",
            "InstallLocation" => @"\\server\share\Example",
            _ => null
        }, machine: false);

        Assert.Equal("Publisher description", app.Description);
        Assert.Null(app.RegisteredDate);
        Assert.Null(app.Drive);
        Assert.Null(app.Publisher);
        Assert.Null(app.EstimatedBytes);
    }

    [Theory]
    [InlineData(@"\\server\share\example.exe,0")]
    [InlineData(@"\\?\C:\Apps\Example.exe,0")]
    [InlineData(@"C:Example.exe")]
    [InlineData(@"C:\Apps\Example.exe:stream")]
    [InlineData(@"C:\Apps\Example.cmd")]
    public void IconsRejectNonLocalOrUnsupportedResources(string icon)
    {
        Assert.Null(WindowsAppIconReader.ParseDesktopIcon(icon));
    }

    [Fact]
    public void IconsUseTheRegisteredResourceIndex()
    {
        Assert.Equal((@"C:\Program Files\Example, Inc\Example.exe", -3),
            WindowsAppIconReader.ParseDesktopIcon("\"C:\\Program Files\\Example, Inc\\Example.exe\",-3"));
    }
}
