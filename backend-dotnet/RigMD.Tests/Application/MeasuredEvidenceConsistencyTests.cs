using System.Text.Json;
using RigMD.Application.Models;
using RigMD.Application.Services;

namespace RigMD.Tests.Application;

public class MeasuredEvidenceConsistencyTests
{
    [Theory]
    [InlineData(84.1, null, "still_active")]
    [InlineData(40, null, "needs_recheck")]
    [InlineData(40, 45d, "resolved")]
    [InlineData(40, 90d, "still_active")]
    public void ComponentCondition_RechecksEverySelectedMetric_NotOnlyTheOriginalCpuFinding(
        double memory, double? temperature, string expected)
    {
        var hardware = Profile(load: 50, memory: memory);
        hardware.Cpu.TemperatureCelsius = temperature;
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", hardware,
            componentIds: "[\"cpu\",\"memory\"]");
        Assert.Equal(expected, result.resolution_status);
        var rows = JsonSerializer.SerializeToElement(result.resolution_proof).EnumerateArray().ToArray();
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "cpu_usage" && p.GetProperty("value").GetString() == "50%");
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "memory_usage");
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "cpu_temperature" &&
            p.GetProperty("status").GetString() == (temperature == null ? "unknown" : temperature >= 85 ? "high" : "normal"));
        Assert.DoesNotContain(rows, p => p.GetProperty("metric_id").GetString() == "storage_usage");
        Assert.Contains("not a hardware-health test", result.resolution_summary);
    }

    [Fact]
    public void CpuOnlyCondition_DoesNotIncludeUnselectedMemoryWarning()
    {
        var hardware = Profile(load: 50, memory: 96);
        hardware.Cpu.TemperatureCelsius = 45;
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", hardware, componentIds: "cpu", diagnosisMode: "component");
        Assert.Equal("resolved", result.resolution_status);
        Assert.DoesNotContain("memory_usage", JsonSerializer.Serialize(result.resolution_proof));
    }

    [Fact]
    public void CpuComponentCondition_RequiresTemperature_WithoutInvalidatingMeasuredActivity()
    {
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", Profile(load: 50),
            componentIds: "cpu", diagnosisMode: "component");
        Assert.Equal("needs_recheck", result.resolution_status);
        var rows = JsonSerializer.SerializeToElement(result.resolution_proof).EnumerateArray().ToArray();
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "cpu_usage" && p.GetProperty("status").GetString() == "normal");
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "cpu_temperature" && p.GetProperty("status").GetString() == "unknown");
    }

    [Fact]
    public void FullCondition_DoesNotIgnoreMemoryWhenTheOriginalFindingWasCpuLoad()
    {
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", Profile(50, 84.1), diagnosisMode: "full");
        Assert.Equal("still_active", result.resolution_status);
        Assert.Contains("memory_usage", JsonSerializer.Serialize(result.resolution_proof));
    }

    [Fact]
    public void ComponentCondition_PreservesUnavailableAndSensorCaptureTimes()
    {
        var hardware = Profile(0, 40);
        hardware.Cpu.UsageAvailable = false;
        var sensorTime = DateTimeOffset.UtcNow.AddSeconds(-4);
        hardware.Cpu.SetTemperatureReading(new CpuTemperatureReading(45, "test sensor", "cpu/0", sensorTime));
        var result = new ResolutionService().CheckResolution("Elevated CPU Utilization", hardware, componentIds: "cpu,memory");
        Assert.Equal("needs_recheck", result.resolution_status);
        var rows = JsonSerializer.SerializeToElement(result.resolution_proof).EnumerateArray().ToArray();
        Assert.Contains(rows, p => p.GetProperty("metric_id").GetString() == "cpu_usage" && p.GetProperty("value").GetString() == "Unavailable");
        Assert.Equal(sensorTime, Assert.Single(rows, p => p.GetProperty("metric_id").GetString() == "cpu_temperature")
            .GetProperty("captured_at").GetDateTimeOffset());
    }

    private static HardwareProfileDto Profile(double load = 22, double memory = 87.3) => new()
    {
        Cpu = new() { Name = "Test CPU", UsagePercent = load, UsageAvailable = true },
        Ram = new() { UsagePercent = memory, UsageAvailable = true, TotalGb = 16, UsedGb = 13.97 },
        Gpu = new() { Name = "Test graphics", HasGpu = true },
        ProcessInsights = new() { BrowserMemoryMb = 3961.5, BrowserProcessCount = 35 },
        StorageDrives = new() { new() { UsagePercent = 74.8, Model = "Test SSD" } }
    };

    [Theory]
    [InlineData(22, "normal")]
    [InlineData(79.99, "normal")]
    [InlineData(80, "high")]
    [InlineData(83, "high")]
    public void WorkloadAndTemperature_AreIndependentMeasurements(double load, string loadStatus)
    {
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "cpu", "memory" }, Hardware = Profile(load)
        });
        var cpu = Assert.Single(result.Proof, p => p.MetricId == "cpu_usage");
        var temperature = Assert.Single(result.Proof, p => p.MetricId == "cpu_temperature");
        Assert.Equal(loadStatus, cpu.Status);
        Assert.DoesNotContain("temperature", cpu.Value);
        Assert.Equal("unknown", temperature.Status);
        Assert.Equal("Unavailable", temperature.Value);
        Assert.Contains(result.Proof, p => p.MetricId == "memory_usage" && p.Status == "high");
    }

    [Theory]
    [InlineData(74.99, 5000, false)]
    [InlineData(75, 1799, false)]
    [InlineData(75, 1800, false)]
    [InlineData(81.99, 0, false)]
    [InlineData(82, 0, true)]
    public void MemoryProofStatus_DiagnosisAndRecheck_Agree(double usage, double browser, bool active)
    {
        var profile = Profile(memory: usage);
        profile.ProcessInsights.BrowserMemoryMb = browser;
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "memory" }, Hardware = profile
        });
        Assert.Equal(active ? "high" : "normal", Assert.Single(result.Proof, p => p.MetricId == "memory_usage").Status);
        var recheck = new ResolutionService().CheckResolution("High Memory Pressure", profile, componentIds: "[\"memory\"]");
        Assert.Equal(active ? "still_active" : "resolved", recheck.resolution_status);
        var row = JsonSerializer.SerializeToElement(recheck.resolution_proof[0]);
        Assert.Equal(active ? "high" : "normal", row.GetProperty("status").GetString());
    }

    [Fact]
    public void MissingGraphicsTemperature_NeverBecomesNormalTemperatureOrStorage()
    {
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput { Hardware = Profile() });
        Assert.Contains(result.Proof, p => p.MetricId == "gpu_temperature" && p.Status == "unknown");
        Assert.Contains(result.Proof, p => p.MetricId == "storage_usage" && p.Value == "74.8% used");
        Assert.Contains(result.Proof, p => p.MetricId == "storage_health" && p.Status == "info" && p.Value == "No warning reported");
        Assert.DoesNotContain(result.Proof, p => p.MetricId == "graphics_devices" && p.Value.Contains("temperature"));
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("memory")]
    public void FailedSample_IsNotMeasuredZeroOrResolved(string component)
    {
        var profile = Profile(memory: 25);
        if (component == "cpu") { profile.Cpu.UsageAvailable = false; profile.Cpu.UsagePercent = 0; }
        else { profile.Ram.UsageAvailable = false; profile.Ram.UsagePercent = 0; }
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { component }, Hardware = profile
        });
        Assert.Contains("unavailable", result.DiagnosedCategory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Proof, p => p.MetricId == (component == "cpu" ? "cpu_usage" : "memory_usage") && p.Status == "unknown" && p.Value == "Unavailable");
        var recheck = new ResolutionService().CheckResolution(component == "cpu" ? "Elevated CPU Utilization" : "High Memory Pressure",
            profile, componentIds: JsonSerializer.Serialize(new[] { component }));
        Assert.Equal("needs_recheck", recheck.resolution_status);
    }

    [Fact]
    public void MissingStorage_CannotBecomeHealthyZeroUse()
    {
        var profile = Profile(memory: 25);
        profile.StorageDrives.Clear();
        var initial = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "storage" }, Hardware = profile
        });
        Assert.Equal("Storage readings unavailable", initial.DiagnosedCategory);
        var recheck = new ResolutionService().CheckResolution(initial.DiagnosedCategory, profile, componentIds: "[\"storage\"]");
        Assert.Equal("needs_recheck", recheck.resolution_status);
        Assert.Contains("Unavailable", JsonSerializer.Serialize(recheck.resolution_proof));
    }

    [Fact]
    public void NewProofRetainsCaptureTimeAndActualMeasuredZero()
    {
        var captured = DateTimeOffset.UtcNow.AddSeconds(-3);
        var profile = Profile(0, 0);
        var result = new AutomaticDiagnosisService().Diagnose(new AutomaticDiagnosisInput
        {
            Mode = "component", ComponentIds = new[] { "cpu", "memory" }, Hardware = profile, CapturedAt = captured
        });
        Assert.All(result.Proof, p => Assert.Equal(captured, p.CapturedAt));
        Assert.Equal("0%", Assert.Single(result.Proof, p => p.MetricId == "cpu_usage").Value);
    }
}
