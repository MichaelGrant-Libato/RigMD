using System.Text.Json;
using RigMD.Api.Services;

namespace RigMD.Tests.Api;

public sealed class WindowsSystemInventoryTests
{
    [Fact]
    public void DriverShowsProviderAndUnsignedStatusWhenWindowsReportsThem()
    {
        using var json = JsonDocument.Parse("""
            {"DeviceID":"PCI\\FIXTURE","DeviceName":"","Description":"Test controller",
             "DriverProviderName":"Example","DriverVersion":"2.3","IsSigned":false}
            """);
        var entry = WindowsSystemInventory.ReadEntry(json.RootElement, "Device drivers", "DeviceID", "DeviceName",
            [("DriverProviderName", "Provider"), ("DriverVersion", "Version"), ("IsSigned", "Signature"), ("Signer", "Signer")]);
        Assert.NotNull(entry);
        Assert.Equal("Test controller", entry.Name);
        Assert.Equal(@"PCI\FIXTURE", entry.Identifier);
        Assert.Contains(new("Signature", "Unsigned"), entry.Fields);
        Assert.DoesNotContain(entry.Fields, field => field.Label == "Signer");
    }

    [Fact]
    public void ServiceShowsStoppedAndManualAsIndependentFields()
    {
        using var json = JsonDocument.Parse("""
            {"Name":"FixtureService","DisplayName":"Example service","State":"Stopped","StartMode":"Manual"}
            """);
        var entry = WindowsSystemInventory.ReadEntry(json.RootElement, "Windows services", "Name", "DisplayName", [("StartMode", "Startup")]);
        Assert.NotNull(entry);
        Assert.Equal("Stopped", entry.State);
        Assert.Equal(new SystemInventoryField("Startup", "Manual"), Assert.Single(entry.Fields));
    }
}
