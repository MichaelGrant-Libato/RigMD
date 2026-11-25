using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class WindowsSoftwareInventoryTests
{
    [Theory]
    [InlineData(@"D:\Programs\Example", @"D:\Programs\Example")]
    [InlineData("\"D:/Programs/Example\"", @"D:\Programs\Example")]
    [InlineData(@"D:relative", null)]
    [InlineData(@"\Program Files\Example", null)]
    [InlineData(@"\\server\share\Example", null)]
    [InlineData(@"\\?\C:\Programs\Example", null)]
    [InlineData(@"C:\Programs\Example:stream", null)]
    [InlineData("", null)]
    public void OnlyAbsoluteLocalInstallPathsAreUsedForAttribution(string input, string? expected) =>
        Assert.Equal(expected, WindowsSoftwareInventory.NormalizeInstallPath(input));

    [Fact]
    public void DriverStoreRecordsAreSeparatedAndLocalizedDetailsArePreserved()
    {
        var records = WindowsSoftwareInventory.ReadDriverStore("""
            Microsoft PnP Utility

            Published Name:   oem19.inf
            Original Name:    oem7.inf
            Provider Name:    Example Audio
            Driver Version:   10/1/2025 1.2.3

            Nombre publicado: oem20.inf
            Nombre original:   teclado.inf
            """).ToArray();

        Assert.Collection(records,
            driver =>
            {
                Assert.Equal("oem19.inf", driver.Inf);
                Assert.Equal("oem7.inf", driver.Name);
                Assert.Equal("Example Audio", driver.Provider);
            },
            driver =>
            {
                Assert.Equal("oem20.inf", driver.Inf);
                Assert.Contains("teclado.inf", driver.Details);
            });
    }
}
