using RigMD.Infrastructure.Windows;
using Xunit;

namespace RigMD.Tests.Infrastructure;

public class WmiOperatingSystemProviderTests
{
    [Fact]
    public void FormatAge_WhenUnder30Days_ReturnsDays()
    {
        var install = new DateTime(2026, 9, 10);
        var now = new DateTime(2026, 9, 21);

        var result = WmiOperatingSystemProvider.FormatAge(install, now);

        Assert.Equal("11 days (Sep 2026)", result);
    }

    [Fact]
    public void FormatAge_WhenSingleDay_ReturnsDaySingular()
    {
        var install = new DateTime(2026, 9, 20);
        var now = new DateTime(2026, 9, 21);

        var result = WmiOperatingSystemProvider.FormatAge(install, now);

        Assert.Equal("1 day (Sep 2026)", result);
    }

    [Fact]
    public void FormatAge_WhenUnder1Year_ReturnsMonths()
    {
        var install = new DateTime(2026, 7, 21);
        var now = new DateTime(2026, 9, 21);

        var result = WmiOperatingSystemProvider.FormatAge(install, now);

        Assert.Equal("2 months (Jul 2026)", result);
    }

    [Fact]
    public void FormatAge_WhenExactlyOneYear_ReturnsOneYearSingular()
    {
        var install = new DateTime(2025, 9, 21);
        var now = new DateTime(2026, 9, 21);

        var result = WmiOperatingSystemProvider.FormatAge(install, now);

        Assert.Equal("1 year (Sep 2025)", result);
    }

    [Fact]
    public void FormatAge_WhenMultipleYears_ReturnsYearsWithTilde()
    {
        var install = new DateTime(2024, 4, 1);
        var now = new DateTime(2026, 9, 21);

        var result = WmiOperatingSystemProvider.FormatAge(install, now);

        Assert.Equal("~2.5 years (Apr 2024)", result);
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public WmiOperatingSystemProviderTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GetSystemAge_OnLiveWindowsMachine_ReturnsNonEmptyValidString()
    {
        var provider = new WmiOperatingSystemProvider();

        var age = provider.GetSystemAge();
        _output.WriteLine($"[Live System Age]: {age}");

        Assert.NotNull(age);
        Assert.NotEqual("Unknown", age);
        Assert.Contains("(", age);
        Assert.Contains(")", age);
    }
}
