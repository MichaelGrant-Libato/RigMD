using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using RigMD.Application.Services;

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
}
