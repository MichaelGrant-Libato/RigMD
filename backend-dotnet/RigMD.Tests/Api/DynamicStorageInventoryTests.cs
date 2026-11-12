using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class DynamicStorageInventoryTests
{
    [Fact]
    public void DifferentComputersGetOnlyTheirOwnProvidedRegistrations()
    {
        StorageAppEntry Desktop(string name, string key, string publisher) => StorageAppInventory.Identify(
            new(name, publisher, "1.0", 1024, "Machine registration"), "machine:" + key, true, key, 1, null);
        var first = Desktop("First device editor", "{1FCA9B41-F341-488F-BB30-12D39E54FF00}", "First Company");
        var second = Desktop("Second device planner", "{B6C235B8-F5A2-4B9A-B49D-739D3D12DC64}", "Second Company");
        Assert.Equal("First device editor", first.Name);
        Assert.Equal("Second device planner", second.Name);
        Assert.NotEqual(first.Id, second.Id);
        Assert.True(first.CanUninstall);
        Assert.True(second.CanUninstall);
        Assert.Equal("desktop", first.SourceType);
    }

    [Fact]
    public void StoreInventoryUsesWindowsMetadataAndDoesNotInventSizeOrRemovalPermission()
    {
        var data = new Dictionary<string, object?> { ["ProgramId"] = "device-specific-package-id", ["Name"] = "A locally installed Store app", ["Vendor"] = "Local publisher", ["Version"] = "2.0" };
        var app = StoreAppInventory.ReadEntry(name => data.GetValueOrDefault(name));
        Assert.NotNull(app);
        Assert.Equal("A locally installed Store app", app.Name);
        Assert.Equal("store", app.SourceType);
        Assert.Null(app.EstimatedBytes);
        Assert.Null(app.ProductCode);
        Assert.False(app.CanUninstall);
        Assert.Contains("scope", app.UninstallReason);
        data["ProgramId"] = "another-device-package-id";
        Assert.NotEqual(app.Id, StoreAppInventory.ReadEntry(name => data.GetValueOrDefault(name))!.Id);
    }

    [Fact]
    public void MissingStoreIdentityCannotBecomeAFakeOrFallbackApp()
    {
        Assert.Null(StoreAppInventory.ReadEntry(_ => null));
        Assert.Null(StoreAppInventory.ReadEntry(name => name == "Name" ? "Only a name" : null));
    }
}
