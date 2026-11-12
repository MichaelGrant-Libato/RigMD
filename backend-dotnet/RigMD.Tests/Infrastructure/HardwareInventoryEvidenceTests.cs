using System.Text.Json;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;
using RigMD.Infrastructure.Windows;
using RigMD.Tests.Application;

namespace RigMD.Tests.Infrastructure;

public sealed class HardwareInventoryEvidenceTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(6)]
    public void MemoryArrayCountsAreReportedInsteadOfAssumingFour(int total)
    {
        var result = WmiMemoryProvider.ReadSlotCounts(() => [8UL * 1024 * 1024 * 1024], () => [total]);
        Assert.Equal(1, result.Used); Assert.Equal(total, result.Total);
    }

    [Fact]
    public void MultipleSystemArraysAreCombinedAndInconsistentTotalsRemainUnknown()
    {
        var result = WmiMemoryProvider.ReadSlotCounts(() => [8UL, 8UL], () => [2, 2]);
        Assert.Equal(2, result.Used); Assert.Equal(4, result.Total);
        result = WmiMemoryProvider.ReadSlotCounts(() => [8UL, 8UL, 8UL], () => [2]);
        Assert.Equal(3, result.Used); Assert.Null(result.Total);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(65536)]
    public void InvalidOrPartialArrayCountsAreNotConvertedToACompleteTotal(int invalid)
    {
        var result = WmiMemoryProvider.ReadSlotCounts(() => [8UL], () => [2, invalid]);
        Assert.Equal(1, result.Used); Assert.Null(result.Total);
    }

    [Fact]
    public void FailedModuleAndArrayQueriesRemainIndependent()
    {
        var result = WmiMemoryProvider.ReadSlotCounts(() => throw new UnauthorizedAccessException(), () => [2]);
        Assert.Null(result.Used); Assert.Equal(2, result.Total);
        result = WmiMemoryProvider.ReadSlotCounts(() => [8UL], () => throw new InvalidOperationException());
        Assert.Equal(1, result.Used); Assert.Null(result.Total);
        result = WmiMemoryProvider.ReadSlotCounts(() => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Null(result.Used); Assert.Null(result.Total);
    }

    [Fact]
    public void EmptyOrPartiallyMissingMemoryInventoryDoesNotBecomeZeroUsedSlots()
    {
        foreach (var modules in new IReadOnlyList<ulong?>[] { [], [8UL, null], [0UL] })
        {
            var result = WmiMemoryProvider.ReadSlotCounts(() => modules, () => [2]);
            Assert.Null(result.Used); Assert.Equal(2, result.Total);
        }
        var missing = WmiMemoryProvider.ReadSlotCounts(() => [], () => []);
        Assert.Null(missing.Used); Assert.Null(missing.Total);
        Assert.Null(WmiMemoryProvider.ReadSlotCounts(() => [8UL], () => [2, null]).Total);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(6)]
    public void DisplayCountUsesTheWindowsDesktopObservation(int count)
        => Assert.Equal(count, new WmiDisplayProvider(() => count).GetConnectedDisplays());

    [Theory]
    [InlineData(0)] [InlineData(-1)]
    public void UnusableDesktopCountsDoNotBecomeOneDisplay(int count)
        => Assert.Null(new WmiDisplayProvider(() => count).GetConnectedDisplays());

    [Fact]
    public void DisplayQueryFailureStaysUnavailable()
        => Assert.Null(new WmiDisplayProvider(() => throw new InvalidOperationException()).GetConnectedDisplays());

    private sealed class GraphicsInventory(int? count) : IGpuProvider, IDisplayProvider
    {
        public GpuStatsDto GetGpuStats() => new() { Name = "Test GPU" };
        public int? GetConnectedDisplays() => count;
        public List<DisplayStatsDto> GetDisplays() => [new(), new(), new()];
    }

    [Theory]
    [InlineData(2)] [InlineData(null)]
    public async Task GraphicsToolDoesNotCountAdapterConfigurationRowsAsScreens(int? count)
    {
        var inventory = new GraphicsInventory(count);
        var tool = new InspectGpuAndDisplaysTool(inventory, inventory);
        using var arguments = JsonDocument.Parse("{}");
        var result = await tool.ExecuteAsync(arguments.RootElement);
        using var json = JsonDocument.Parse(result.DataJson!);
        var value = json.RootElement.GetProperty("connectedDisplaysCount");
        if (count.HasValue) Assert.Equal(count.Value, value.GetInt32()); else Assert.Equal(JsonValueKind.Null, value.ValueKind);
        Assert.Equal(3, json.RootElement.GetProperty("displays").GetArrayLength());
        Assert.Contains("not a screen count", result.Summary);
    }

    [Fact]
    public void UnknownHardwareCountsSurviveSerializationWithoutInventedNumbers()
    {
        var hardware = new HardwareProfileDto();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(hardware, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("connectedDisplays").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ram").GetProperty("slotsUsed").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("ram").GetProperty("slotsTotal").ValueKind);
    }

    [Fact]
    public void MissingDisplayCountRemainsUnknownInNewAndLegacyFollowUps()
    {
        var hardware = ScannerEvidenceAuditTests.KnownReadings(); hardware.ConnectedDisplays = null;
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["display"], Hardware = hardware });
        var reading = Assert.Single(result.Proof, p => p.MetricId == "display_count");
        Assert.Equal("unknown", reading.Status); Assert.Equal("unavailable", reading.EvidenceKind);
        Assert.Equal("Scanner evidence unavailable", result.DiagnosedCategory);
        var resolution = new ResolutionService();
        Assert.Equal("needs_recheck", resolution.CheckResolution("Display driver behavior", hardware, scenarioId: "no-display").resolution_status);
        var legacy = resolution.CheckResolution("Display driver behavior", hardware);
        Assert.Equal("needs_recheck", legacy.resolution_status);
        Assert.Contains("could not be verified", legacy.resolution_summary);
    }

    [Fact]
    public void AConfirmedMonitorErrorRemainsActiveEvenWhenDisplayCountingFails()
    {
        var hardware = ScannerEvidenceAuditTests.KnownReadings(); hardware.ConnectedDisplays = null;
        hardware.DeviceErrors.Add(new() { Name = "Test screen", PnpClass = "Monitor", ErrorCode = 43 });
        var result = new AutomaticDiagnosisService().Diagnose(new() { Mode = "component", ComponentIds = ["display"], Hardware = hardware });
        Assert.Equal("Device status warning", result.DiagnosedCategory);
        Assert.Contains(result.Proof, p => p.MetricId == "display_count" && p.Status == "unknown");
        Assert.Equal("still_active", new ResolutionService().CheckResolution("Display driver behavior", hardware, scenarioId: "no-display").resolution_status);
    }

    [Theory]
    [InlineData("Display driver behavior", null)]
    [InlineData("Display driver behavior", 0)]
    [InlineData("Display driver behavior", -1)]
    [InlineData("Display or Driver Instability", null)]
    public void LegacyDisplayCategoriesRequireAUsableCount(string category, int? count)
    {
        var hardware = ScannerEvidenceAuditTests.KnownReadings();
        hardware.ConnectedDisplays = count;
        var result = new ResolutionService().CheckResolution(category, hardware);
        Assert.Equal("needs_recheck", result.resolution_status);
        using var proof = JsonDocument.Parse(JsonSerializer.Serialize(result.resolution_proof));
        var reading = Assert.Single(proof.RootElement.EnumerateArray(), p => p.GetProperty("metric_id").GetString() == "display_count");
        Assert.Equal("unknown", reading.GetProperty("status").GetString());
        Assert.Equal("unavailable", reading.GetProperty("evidence_kind").GetString());
    }

    [Fact]
    public void LegacyDisplayFollowUpKeepsConfirmedErrorsAndDriverScope()
    {
        var hardware = ScannerEvidenceAuditTests.KnownReadings();
        var service = new ResolutionService();
        hardware.ConnectedDisplays = 2;
        Assert.Equal("resolved", service.CheckResolution("Display driver behavior", hardware).resolution_status);
        hardware.ConnectedDisplays = null;
        Assert.Equal("resolved", service.CheckResolution("Driver conflict", hardware).resolution_status);
        hardware.DeviceErrors.Add(new() { Name = "Test screen", PnpClass = "Monitor", ErrorCode = 43 });
        Assert.Equal("still_active", service.CheckResolution("Display driver behavior", hardware).resolution_status);
    }
}
