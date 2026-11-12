using RigMD.Api.Services;
using Microsoft.Win32;

namespace RigMD.Tests.Api;

public class StorageAppInventoryTests
{
    [Fact]
    public void SharedPerUserRegistrationsAreReadOnlyOnceAndHaveOneIdentity()
    {
        Assert.Equal(new[] { RegistryView.Default }, StorageAppInventory.ViewsFor(RegistryHive.CurrentUser, true));
        Assert.Equal(new[] { RegistryView.Default }, StorageAppInventory.ViewsFor(RegistryHive.CurrentUser, false));
        Assert.Equal(StorageAppInventory.RegistrationIdentity(RegistryHive.CurrentUser, RegistryView.Registry64, "Riot Game"),
            StorageAppInventory.RegistrationIdentity(RegistryHive.CurrentUser, RegistryView.Registry32, "riot game"));
    }

    [Fact]
    public void GenuineMachineArchitecturesAndDifferentInstallationsKeepSeparateIdentities()
    {
        Assert.Equal(new[] { RegistryView.Registry64, RegistryView.Registry32 }, StorageAppInventory.ViewsFor(RegistryHive.LocalMachine, true));
        Assert.NotEqual(StorageAppInventory.RegistrationIdentity(RegistryHive.LocalMachine, RegistryView.Registry64, "App"),
            StorageAppInventory.RegistrationIdentity(RegistryHive.LocalMachine, RegistryView.Registry32, "App"));
        Assert.NotEqual(StorageAppInventory.RegistrationIdentity(RegistryHive.CurrentUser, RegistryView.Default, "App"),
            StorageAppInventory.RegistrationIdentity(RegistryHive.CurrentUser, RegistryView.Default, "Different App"));
    }

    [Fact]
    public void InventoryUsesOnlyDisplayMetadataAndInstallerEstimate()
    {
        var values = new Dictionary<string, object?>
        {
            ["DisplayName"] = "Example\nApp", ["Publisher"] = "Publisher", ["DisplayVersion"] = "1.0",
            ["EstimatedSize"] = 2048
        };
        object? Read(string name)
        {
            Assert.DoesNotContain(name, new[] { "UninstallString", "QuietUninstallString", "InstallLocation", "DisplayIcon" });
            return values.GetValueOrDefault(name);
        }
        var app = StorageAppInventory.ReadEntry(Read, "Machine registration");
        Assert.NotNull(app);
        Assert.Equal("ExampleApp", app.Name);
        Assert.Equal(2097152L, app.EstimatedBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData("1024")]
    public void MissingOrInvalidSizeStaysUnknown(object? size)
    {
        var app = StorageAppInventory.ReadEntry(name => name switch { "DisplayName" => "App", "EstimatedSize" => size, _ => null }, "Machine");
        Assert.NotNull(app);
        Assert.Null(app.EstimatedBytes);
    }

    [Theory]
    [InlineData("SystemComponent", 1)]
    [InlineData("ParentKeyName", "Parent")]
    [InlineData("ReleaseType", "Security Update")]
    public void SystemAndChildRegistrationsAreNotOfferedAsApps(string field, object value)
    {
        Assert.Null(StorageAppInventory.ReadEntry(name => name == "DisplayName" ? "App" : name == field ? value : null, "Machine"));
    }
}
