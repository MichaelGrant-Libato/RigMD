using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RigMD.Api.Controllers;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Ai;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Application.Services;
using RigMD.Infrastructure.Persistence;

namespace RigMD.Tests.Api;

public class DiagnosticResolutionTests
{
    [Theory]
    [InlineData("full")]
    [InlineData("component")]
    [InlineData("scenario")]
    public async Task LocalScanReturnsTheSameExplicitScopeThatWasSaved(string mode)
    {
        var options = new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("DataSource=:memory:").Options;
        await using var db = new RigMdDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var repo = new DiagnosticSessionRepository(db, new TestClient("local"));
        var controller = new DiagnosticController(null!, repo, new TestProfile(new HardwareProfileDto()),
            new ResolutionService(), new AutomaticDiagnosisService());
        var components = mode == "component" ? new[] { "memory" } : Array.Empty<string>();
        var scenario = mode == "scenario" ? "app-crashes" : null;
        var response = Assert.IsType<OkObjectResult>(await controller.LocalDiagnosis(new()
        {
            Mode = mode, ComponentIds = components, ScenarioId = scenario
        }, CancellationToken.None));
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(mode, body.GetProperty("diagnosis_mode").GetString());
        Assert.Equal(components, body.GetProperty("component_ids").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(scenario, body.GetProperty("scenario_id").GetString());
        db.ChangeTracker.Clear();
        var saved = await repo.GetSessionAsync(Guid.Parse(body.GetProperty("session_id").GetString()!));
        Assert.Equal(mode, saved!.DiagnosisMode);
        Assert.Equal(components, JsonSerializer.Deserialize<string[]>(saved.ComponentIds));
        Assert.Equal(scenario ?? "", saved.ScenarioId ?? "");
    }

    [Theory]
    [InlineData("local", null, "rate-limited")]
    [InlineData("gemini", "gemini-test-version", "completed")]
    public async Task SavedCheckIsAvailableBeforeExplanationAndPersistsOutcome(string source, string? model, string status)
    {
        var options = new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("DataSource=:memory:").Options;
        await using var db = new RigMdDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var repo = new DiagnosticSessionRepository(db, new TestClient("local"));
        var explainer = new TestExplainer(source, model, status);
        var hardware = new HardwareProfileDto { Cpu = new() { UsagePercent = 95, UsageAvailable = true } };
        var controller = new DiagnosticController(null!, repo, new TestProfile(hardware),
            new ResolutionService(), new AutomaticDiagnosisService(), explainer);
        var response = Assert.IsType<OkObjectResult>(await controller.LocalDiagnosis(
            new DiagnosticController.LocalDiagnosisRequest { Mode = "component", ComponentIds = new[] { "cpu" } }, CancellationToken.None));
        var body = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal("local", body.GetProperty("explanation_source").GetString());
        Assert.Equal(0, explainer.Calls);
        var id = Guid.Parse(body.GetProperty("session_id").GetString()!);
        var initial = (await repo.GetSessionAsync(id))!;
        var localText = initial.AiExplanation;
        Assert.NotEmpty(localText);
        // A pending session represents a scan saved while the optional provider was enabled.
        await repo.SaveExplanationAsync(id, "local", null, "pending");
        Assert.IsType<OkObjectResult>(await controller.ExplainSavedCheck(id, CancellationToken.None));
        Assert.IsType<OkObjectResult>(await controller.ExplainSavedCheck(id, CancellationToken.None));
        Assert.Equal(1, explainer.Calls);
        db.ChangeTracker.Clear();
        var saved = await repo.GetSessionAsync(id);
        Assert.Equal(source, saved!.ExplanationSource);
        Assert.Equal(model ?? "", saved.ExplanationModel);
        Assert.Equal(status, saved.ExplanationStatus);
        Assert.Equal(source == "gemini" ? "Simulated explanation for a provenance test." : localText, saved.AiExplanation);
    }

    [Fact]
    public void CloudEvidenceKeepsNumericMeasurementsAndDropsDeviceNamesAndPaths()
    {
        var proof = JsonSerializer.Deserialize<JsonElement[]>("""
            [
              {"metric_id":"storage_usage","label":"Private name","value":"84.8% used","status":"high"},
              {"metric_id":"graphics_devices","label":"Adapter","value":"Contoso Graphics","status":"info"},
              {"metric_id":"storage_usage","label":"Storage","value":"C:\\private","status":"high"},
              {"metric_id":"cpu_temperature","value":"0°C","status":"unknown"}
            ]
            """)!;
        var reading = Assert.Single(DiagnosticController.CloudEvidence(proof.Cast<object>().ToArray()));
        Assert.Equal("Storage space used", reading.Label);
        Assert.Equal("84.8% used", reading.Value);
    }

    private sealed class TestExplainer(string source, string? model, string status) : IAiExplainer
    {
        public int Calls { get; private set; }

        public Task<string> GenerateExplanationAsync(RigMD.Domain.Rules.DiagnosticResult result, RigMD.Domain.Rules.DiagnosticSymptomPayload symptom)
            => Task.FromResult("Simulated explanation for a provenance test.");
        public async Task<DiagnosticExplanation> GenerateExplanationWithSourceAsync(RigMD.Domain.Rules.DiagnosticResult result, RigMD.Domain.Rules.DiagnosticSymptomPayload symptom)
        {
            Calls++;
            return new(await GenerateExplanationAsync(result, symptom), source, model, status);
        }
    }

    [Theory]
    [InlineData("component", 84.1, null, "still_active")]
    [InlineData("component", 40d, null, "needs_recheck")]
    [InlineData("component", 40d, 45d, "resolved")]
    [InlineData("full", 84.1, null, "still_active")]
    public async Task Recheck_PreservesTheSavedScopeAndOriginalEvidence(
        string mode, double memory, double? temperature, string expectedStatus)
    {
        var options = new DbContextOptionsBuilder<RigMdDbContext>().UseSqlite("DataSource=:memory:").Options;
        await using var db = new RigMdDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var repo = new DiagnosticSessionRepository(db, new TestClient());
        var hardware = new HardwareProfileDto
        {
            Cpu = new() { UsagePercent = 50, UsageAvailable = true, TemperatureCelsius = temperature },
            Ram = new() { UsagePercent = memory, UsageAvailable = true, TotalGb = 16 },
            ProcessInsights = new() { BrowserMemoryMb = 500, BrowserProcessCount = 2 }
        };
        var original = new[] { new AutomaticDiagnosisProof
        {
            MetricId = "cpu_usage", Label = "Processor activity", Value = "87%", Status = "high"
        } };
        var id = await repo.SaveAutomaticDiagnosisAsync(hardware, mode,
            mode == "component" ? new[] { "cpu", "memory" } : Array.Empty<string>(),
            null, Guid.Empty, "test", "Elevated CPU Utilization", "Troubleshoot", "Medium", "Saved high workload",
            "test-client", proof: original);
        var controller = new DiagnosticController(null!, repo, new TestProfile(hardware),
            new ResolutionService(), new AutomaticDiagnosisService());
        var response = Assert.IsType<OkObjectResult>(await controller.CheckResolution(id.ToString()));
        var returned = JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal(expectedStatus, returned.GetProperty("resolution_status").GetString());
        Assert.Equal("complete", returned.GetProperty("follow_up_history_status").GetString());
        Assert.Single(returned.GetProperty("follow_up_history").EnumerateArray());
        Assert.Contains(returned.GetProperty("resolution_proof").EnumerateArray(),
            p => p.GetProperty("metric_id").GetString() == "memory_usage");
        db.ChangeTracker.Clear();
        var saved = await repo.GetSessionAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(expectedStatus, saved.ResolutionStatus);
        Assert.Equal("87%", JsonSerializer.SerializeToElement(Assert.Single(saved.Proof)).GetProperty("value").GetString());
        Assert.Contains(saved.ResolutionProof, p => JsonSerializer.SerializeToElement(p).GetProperty("metric_id").GetString() == "memory_usage");
    }

    private sealed class TestClient(string clientId = "test-client") : ICurrentClientProvider
    {
        public string GetCurrentClientId() => clientId;
    }

    private sealed class TestProfile(HardwareProfileDto hardware) : IWindowsSystemProfileService
    {
        public HardwareProfileDto GetLiveSystemProfile() => hardware;
    }
}
