using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using RigMD.Domain.Rules;

namespace RigMD.Tests.Domain;

public class DiagnosticEngineTests
{
    [Fact]
    public void RunDiagnostic_WhenStorageSymptomSelected_SetsStorageCategoryAndHighConfidence()
    {
        // Arrange
        var payload = new DiagnosticSymptomPayload
        {
            SymptomType = "Storage",
            MentionsStorage = true,
            Severity = "high",
            Frequency = "frequent",
            WarningSigns = "Access denied",
            WarningSignsLabel = "Access denied"
        };
        var metrics = new HardwareMetrics();

        // Act
        var result = DiagnosticEngine.RunDiagnostic(payload, metrics);

        // Assert
        Assert.Equal("Storage health behavior", result.DiagnosedCategory);
        Assert.Equal("High", result.ConfidenceLabel);
        Assert.Equal("Maintain", result.ActionCategory);
    }

    [Fact]
    public void RunDiagnostic_WhenHardwareTelemetryIndicatesHighCpu_OverridesOrAddsToEvidence()
    {
        // Arrange
        var payload = new DiagnosticSymptomPayload
        {
            SymptomType = "unknown"
        };
        
        var metrics = new HardwareMetrics
        {
            CpuUsagePercent = 90 // High CPU load
        };

        // Act
        var result = DiagnosticEngine.RunDiagnostic(payload, metrics);

        // Assert
        Assert.Equal("OS performance degradation", result.DiagnosedCategory);
        Assert.Contains(result.Evidence, e => e.Category == "OS performance degradation" && e.Points == 4);
    }

    [Fact]
    public void RunDiagnostic_WhenBootFailureWithSeverity_SetsEscalateActionAndHighConfidence()
    {
        // Arrange
        var payload = new DiagnosticSymptomPayload
        {
            SymptomType = "Boot and startup failure",
            Severity = "high",
            MentionsNoBoot = true
        };
        
        var metrics = new HardwareMetrics();

        // Act
        var result = DiagnosticEngine.RunDiagnostic(payload, metrics);

        // Assert
        Assert.Equal("Boot and startup failure", result.DiagnosedCategory);
        Assert.Equal("High", result.ConfidenceLabel);
        Assert.Equal("Escalate for Professional Inspection", result.ActionCategory);
    }

    [Fact]
    public void RunDiagnostic_WhenEmptyPayload_ReturnsMonitorAndNoActiveIssue()
    {
        // Arrange
        var payload = new DiagnosticSymptomPayload();
        var metrics = new HardwareMetrics();

        // Act
        var result = DiagnosticEngine.RunDiagnostic(payload, metrics);

        // Assert
        Assert.Equal("No active issue detected", result.DiagnosedCategory);
        Assert.Equal("High", result.ConfidenceLabel);
        Assert.Equal("Monitor", result.ActionCategory);
    }
}
