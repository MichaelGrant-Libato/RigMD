using System.Reflection;
using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Tests.Infrastructure;

public class WmiUsageAvailabilityTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("missing", null)]
    [InlineData("-1", null)]
    [InlineData("NaN", null)]
    [InlineData("Infinity", null)]
    [InlineData("0", 0d)]
    [InlineData("22", 22d)]
    public void CpuCounterParsing_PreservesZeroButRejectsMissingOrInvalidValues(string? input, double? expected)
    {
        var parser = typeof(WmiCpuProvider).GetMethod("ReadUsage", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = parser.Invoke(null, new object?[] { input });
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnsampledLiveCpuAndMemory_AreNull_NotZero()
    {
        var payload = JsonSerializer.SerializeToElement(new TelemetryUpdateDto());
        foreach (var key in new[] { "CpuUsagePercent", "RamUsagePercent", "RamUsedGb" })
            Assert.Equal(JsonValueKind.Null, payload.GetProperty(key).ValueKind);
    }
}
