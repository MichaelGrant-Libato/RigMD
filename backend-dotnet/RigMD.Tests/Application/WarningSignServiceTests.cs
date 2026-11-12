using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using RigMD.Application.Services;
using System.Text.Json;

namespace RigMD.Tests.Application;

public class WarningSignServiceTests
{
    [Fact]
    public void BuildReference_ParsesAndMapsSymptomsToStandardWarningSigns()
    {
        // Arrange
        var service = new WarningSignService();
        var observedTexts = new[] { "blue screen", "system is stuttering", "random words" };

        // Act
        var result = service.BuildReference(observedTexts);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.summary.observed_warning_signs);

        var bsodRow = result.warning_signs.FirstOrDefault(w => w.id == "WS-002");
        Assert.NotNull(bsodRow);
        Assert.True(bsodRow.observed);
        Assert.Equal(1, bsodRow.observed_count);

        var stutterRow = result.warning_signs.FirstOrDefault(w => w.id == "WS-001");
        Assert.NotNull(stutterRow);
        Assert.True(stutterRow.observed);
    }

    [Fact]
    public void BuildReference_WhenFilteringByCategory_ReturnsOnlyMatchingRows()
    {
        // Arrange
        var service = new WarningSignService();
        var observedTexts = Array.Empty<string>();

        // Act
        var result = service.BuildReference(observedTexts, category: "Storage");

        // Assert
        Assert.NotNull(result);
        Assert.All(result.warning_signs, row => Assert.Equal("Storage", row.category));
        Assert.True(result.warning_signs.Count > 0);
    }

    [Fact]
    public void BuildReference_WhenObservedOnly_ReturnsOnlyObservedRows()
    {
        // Arrange
        var service = new WarningSignService();
        var observedTexts = new[] { "loud fan" };

        // Act
        var result = service.BuildReference(observedTexts, observedOnly: true);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.warning_signs);
        Assert.Equal("WS-007", result.warning_signs[0].id); // Loud Fan Noise or Heat
    }

    [Fact]
    public void SplitWarningSigns_DeduplicatesAndSplitsByDelimiters()
    {
        // Arrange
        var rawWarningSigns = "bsod, stuttering; slow boot";

        // Act
        var result = WarningSignService.SplitWarningSigns(rawWarningSigns);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Contains("bsod", result);
        Assert.Contains("stuttering", result);
        Assert.Contains("slow boot", result);
    }

    [Theory]
    [InlineData("memory_usage", "WS-011")]
    [InlineData("cpu_usage", "WS-012")]
    [InlineData("storage_usage", "WS-013")]
    [InlineData("storage_usage_C:", "WS-013")]
    [InlineData("storage_usage_d:\\", "WS-013")]
    [InlineData("storage_health", "WS-006")]
    [InlineData("cpu_temperature", "WS-007")]
    [InlineData("gpu_temperature", "WS-007")]
    [InlineData("cpu_throttling", "WS-007")]
    [InlineData("system_crash_history", "WS-018")]
    [InlineData("application_crash_history", "WS-009")]
    [InlineData("network_connectivity", "WS-014")]
    [InlineData("network_adapter", "WS-014")]
    [InlineData("network_address", "WS-014")]
    [InlineData("network_gateway", "WS-014")]
    [InlineData("network_dns_config", "WS-014")]
    [InlineData("network_dns", "WS-014")]
    [InlineData("network_latency", "WS-014")]
    [InlineData("network_loss", "WS-014")]
    [InlineData("network_signal", "WS-014")]
    [InlineData("device_error_0123456789ABCDEF01234567", "WS-015")]
    [InlineData("device_error_0", "WS-015")]
    [InlineData("memory_app_warning", "WS-016")]
    [InlineData("battery_charge", "WS-017")]
    public void SavedProof_MapsOnlySupportedMetricWarnings(string metric, string expected)
    {
        foreach (var status in new[] { "high", "warning", "critical", "elevated", "still needs attention" })
        {
            // Older saved proof does not always contain evidence_kind or label/value.
            var proof = JsonSerializer.Serialize(new[] { new { metric_id = metric, status } });
            Assert.Equal(new[] { expected }, WarningSignService.GetSessionReferenceIds(null, proof));
        }
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("unknown")]
    [InlineData("info")]
    [InlineData("unavailable")]
    [InlineData("not_assessed")]
    [InlineData("not_verified")]
    [InlineData("unverified")]
    [InlineData("resolved")]
    [InlineData("")]
    public void SavedProof_NormalOrUnknownNeverBecomesAnAlertFromItsProse(string status)
    {
        var proof = JsonSerializer.Serialize(new[] { "memory_usage", "cpu_usage", "storage_health", "storage_usage", "network_loss" }
            .Select(metric => new { metric_id = metric, status, label = "SMART caution and blue screen", value = "Warning", meaning = "Overheating" }));
        Assert.Empty(WarningSignService.GetSessionReferenceIds(null, proof));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("not_assessed")]
    [InlineData("context")]
    [InlineData("unverified")]
    public void SavedProof_ExplicitUnsupportedEvidenceKindCannotJustifyAWarning(string kind)
    {
        var proof = JsonSerializer.Serialize(new[] { new { metric_id = "storage_health", status = "high", evidence_kind = kind } });
        Assert.Empty(WarningSignService.GetSessionReferenceIds(null, proof));
    }

    [Theory]
    [InlineData("storage_usage_unidentified")]
    [InlineData("storage_usage_C:\\other")]
    [InlineData("network_arbitrary")]
    [InlineData("graphics_devices")]
    [InlineData("display_count")]
    [InlineData("device_error_arbitrary")]
    [InlineData("something_else")]
    [InlineData("")]
    public void SavedProof_UnknownMetricOrContextDoesNotUseLabelOrMeaning(string metric)
    {
        var proof = JsonSerializer.Serialize(new[] { new { metric_id = metric, status = "critical", label = "SMART caution", meaning = "blue screen" } });
        Assert.Empty(WarningSignService.GetSessionReferenceIds(null, proof));
    }

    [Theory]
    [InlineData("SMART OK")]
    [InlineData("No blue screen")]
    [InlineData("no BSOD")]
    [InlineData("No SMART caution")]
    [InlineData("SMART warning resolved")]
    [InlineData("No warning signs")]
    [InlineData("None")]
    [InlineData("Windows Update")]
    [InlineData("bad")]
    [InlineData("storage space is high")]
    [InlineData("Snapshot captured")]
    public void Legacy_UnknownNegatedOrNormalTextDoesNotBecomeAWarning(string text)
    {
        Assert.Equal(0, new WarningSignService().BuildReference([text]).summary.observed_warning_signs);
    }

    [Fact]
    public void Legacy_ExactAliasesNamesAndIdsCountOncePerSessionAndKeepRepetition()
    {
        var result = new WarningSignService().BuildReference([
            "SMART caution; WS-006; smart warning; Storage SMART Caution or File Access Error",
            "WS-006",
            "blue screen, BSOD; Blue Screen or Crash Code"
        ], observedOnly: true);
        Assert.Equal(2, result.warning_signs.Single(w => w.id == "WS-006").observed_count);
        Assert.Equal(1, result.warning_signs.Single(w => w.id == "WS-002").observed_count);
        Assert.Equal(2, result.summary.observed_warning_signs);
        Assert.Equal(3, result.summary.total_observed_occurrences);
        Assert.Equal("WS-010", Assert.Single(new WarningSignService().GetObservedReferenceRows(["startup delay"])).id);
        Assert.Equal("WS-004", Assert.Single(new WarningSignService().GetObservedReferenceRows(["No Display"])).id);
        Assert.Equal("WS-005", Assert.Single(new WarningSignService().GetObservedReferenceRows(["No boot device"])).id);
    }

    [Fact]
    public void SavedProof_CombinesLegacyAndStructuredEvidenceWithoutDuplicateReferences()
    {
        const string proof = """
            [null, 7, {"metric_id":"cpu_temperature","status":"high","evidence_kind":"measurement"},
             {"metric_id":"gpu_temperature","status":"critical","evidence_kind":"measurement"},
             {"metric_id":"storage_usage_C:","status":"warning"},
             {"metric_id":"storage_usage_D:","status":"high"}]
            """;
        var ids = WarningSignService.GetSessionReferenceIds("loud fan; WS-007", proof);
        Assert.Equal(new[] { "WS-007", "WS-013" }, ids);
        Assert.DoesNotContain("WS-006", ids);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[{\"metric_id\":false,\"status\":\"high\"}]")]
    [InlineData("[{\"metric_id\":\"storage_health\",\"status\":7}]")]
    public void SavedProof_MalformedOrWrongShapeDoesNotInventWarningsOrLoseKnownLegacy(string proof)
    {
        Assert.Equal(new[] { "WS-002" }, WarningSignService.GetSessionReferenceIds("bsod", proof));
        Assert.Empty(WarningSignService.GetSessionReferenceIds(null, proof));
    }

    [Fact]
    public void SavedProof_AcceptsKnownOlderPropertyConventions()
    {
        Assert.Equal(new[] { "WS-011" }, WarningSignService.GetSessionReferenceIds(null,
            "[{\"MetricId\":\"memory_usage\",\"Status\":\"High\",\"EvidenceKind\":\"measurement\"}]"));
        Assert.Equal(new[] { "WS-006" }, WarningSignService.GetSessionReferenceIds(null,
            "[{\"metricId\":\"storage_health\",\"status\":\"warning\",\"evidenceKind\":\"reported_status\"}]"));
    }

    [Fact]
    public void SavedSystemEventRecordsDoNotClaimAnObservedBlueScreen()
    {
        var ids = WarningSignService.GetSessionReferenceIds(null,
            "[{\"metric_id\":\"system_crash_history\",\"status\":\"high\",\"value\":\"1 found\",\"evidence_kind\":\"event_log\"}]");
        var result = new WarningSignService().GetObservedReferenceRows([string.Join("; ", ids)]);
        Assert.Equal("WS-018", Assert.Single(result).id);
        Assert.DoesNotContain(result, row => row.id == "WS-002");
    }
}
