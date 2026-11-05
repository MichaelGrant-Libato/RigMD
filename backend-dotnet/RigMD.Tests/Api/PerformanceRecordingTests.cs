using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Api.Controllers;
using RigMD.Api.Middleware;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Common;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Models;
using RigMD.Domain.Entities;

namespace RigMD.Tests.Api;

public sealed class PerformanceRecordingTests
{
    [Theory]
    [InlineData("startup.api_start.desktop", true)]
    [InlineData("startup.api_ready.desktop", true)]
    [InlineData("startup.webview.desktop", true)]
    [InlineData("startup.dashboard.desktop", true)]
    [InlineData("startup.unknown.desktop", false)]
    public void StartupStagesUseAnExplicitAllowlist(string operation, bool expected)
    {
        Assert.Equal(expected, PerformanceRules.IsClientOperation(operation));
        Assert.Equal(expected, PerformanceRules.IsValid(new PerformanceSample(Guid.NewGuid(), operation, "completed", 100)));
    }

    private sealed class Client : ICurrentClientProvider { public string GetCurrentClientId() => "local-owner"; }
    private sealed class Records : IPerformanceRecordRepository
    {
        public readonly List<PerformanceRecord> Rows = [];
        public bool Fail;
        public Task AddAsync(PerformanceRecord row, CancellationToken cancellationToken = default)
        { if (Fail) throw new IOException("private path or secret"); lock (Rows) Rows.Add(row); return Task.CompletedTask; }
        public Task<IReadOnlyList<PerformanceRecord>> GetAsync(string id, CancellationToken cancellationToken = default)
        { lock (Rows) return Task.FromResult<IReadOnlyList<PerformanceRecord>>(Rows.Where(r => r.ClientId == id).ToArray()); }
    }
    private static ServiceProvider Services(Records repository) => new ServiceCollection().AddSingleton<IPerformanceRecordRepository>(repository).BuildServiceProvider();
    private static PerformanceRecorder Recorder(ServiceProvider provider) => new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<PerformanceRecorder>.Instance);
    private static DefaultHttpContext Context(string method = "POST", string path = "/api/diagnosis/local-scan")
    {
        var context = new DefaultHttpContext(); context.Request.Method = method; context.Request.Path = path;
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "local-owner")], "test"));
        return context;
    }

    [Theory]
    [InlineData("GET", "/api/diagnosis/sessions/", "history.backend")]
    [InlineData("POST", "/api/diagnosis/local-scan", "scan.backend")]
    [InlineData("POST", "/api/diagnosis/00000000-0000-0000-0000-000000000001/check-resolution", "recheck.backend")]
    [InlineData("POST", "/api/autonomy/execute", "action_request.backend")]
    [InlineData("POST", "/api/performance", null)]
    [InlineData("GET", "/api/performance", null)]
    [InlineData("POST", "/api/cloud-ai/connect", null)]
    [InlineData("GET", "/api/diagnosis/sessions/private-id", null)]
    public void OnlyFixedOperationsAreRecordedWithoutUrlsOrIds(string method, string path, string? expected) => Assert.Equal(expected, PerformanceRecordingMiddleware.Operation(method, path));

    [Theory]
    [InlineData("completed", 200, 0)]
    [InlineData("incomplete", 200, 2)]
    [InlineData("failed", 429, 0)]
    [InlineData("failed", 500, 0)]
    public async Task MiddlewareRecordsOutcomesAndCannotModifyDiagnosticResponse(string outcome, int status, int missing)
    {
        var rows = new Records(); using var provider = Services(rows); using var recorder = Recorder(provider); await recorder.StartAsync(default);
        var context = Context(); context.Request.QueryString = new("?token=never-export");
        context.Response.Body = new MemoryStream();
        var middleware = new PerformanceRecordingMiddleware(async ctx => {
            ctx.Items[PerformanceRecordingMiddleware.ContextKey] = new PerformanceContext(3, missing, "full", "local");
            ctx.Response.StatusCode = status; await ctx.Response.WriteAsync("original response");
        });
        await middleware.InvokeAsync(context, recorder); await recorder.StopAsync(default);
        var row = Assert.Single(rows.Rows); Assert.Equal(outcome, row.Outcome); Assert.Equal(status, row.HttpStatus);
        Assert.Equal(3, row.RecordCount); Assert.Equal(missing, row.UnavailableCount); Assert.True(row.DurationMs >= 0);
        Assert.DoesNotContain("never-export", JsonSerializer.Serialize(row));
        context.Response.Body.Position = 0; Assert.Equal("original response", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task ExceptionsAndCancellationAreRecordedWithoutSwallowingTheOriginalException()
    {
        var rows = new Records(); using var provider = Services(rows); using var recorder = Recorder(provider); await recorder.StartAsync(default);
        var middleware = new PerformanceRecordingMiddleware(_ => throw new InvalidOperationException("private failure"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context(), recorder));
        var canceled = Context(); canceled.RequestAborted = new CancellationToken(true);
        await new PerformanceRecordingMiddleware(_ => Task.CompletedTask).InvokeAsync(canceled, recorder);
        await recorder.StopAsync(default);
        Assert.Equal(new[] { "failed", "canceled" }, rows.Rows.Select(r => r.Outcome));
    }

    [Fact]
    public async Task StorageFailureAndFullQueueAreVisibleButDoNotBreakRequests()
    {
        var rows = new Records { Fail = true }; using var provider = Services(rows); using var recorder = Recorder(provider);
        var accepted = Enumerable.Range(0, 257).Select(_ => recorder.TryRecord("owner", new(Guid.NewGuid(), "history.ui", "completed", 20, 50))).ToArray();
        Assert.Equal(256, accepted.Count(x => x)); Assert.False(accepted[^1]);
        await recorder.StartAsync(default); await recorder.StopAsync(default);
        var health = JsonSerializer.SerializeToElement(recorder.Health);
        Assert.Equal(1, health.GetProperty("dropped").GetInt32()); Assert.Equal(256, health.GetProperty("storageFailures").GetInt32());
        Assert.Equal(0, health.GetProperty("saved").GetInt32());
    }

    [Fact]
    public async Task UnauthorizedAndUnrelatedRequestsDoNotProduceRecords()
    {
        var rows = new Records(); using var provider = Services(rows); using var recorder = Recorder(provider); await recorder.StartAsync(default);
        var context = Context(); context.User = new ClaimsPrincipal(new ClaimsIdentity());
        await new PerformanceRecordingMiddleware(_ => Task.CompletedTask).InvokeAsync(context, recorder);
        await new PerformanceRecordingMiddleware(_ => Task.CompletedTask).InvokeAsync(Context("GET", "/api/performance"), recorder);
        await recorder.StopAsync(default); Assert.Empty(rows.Rows);
    }

    [Fact]
    public async Task ClientCannotSpoofBackendMeasurementsAndExportsOmitIdentity()
    {
        var rows = new Records(); using var provider = Services(rows); using var recorder = Recorder(provider); await recorder.StartAsync(default);
        var controller = new PerformanceController(rows, recorder, new Client(), NullLogger<PerformanceController>.Instance);
        Assert.IsType<BadRequestObjectResult>(controller.Save(new(Guid.NewGuid(), "scan.backend", "completed", 1)));
        Assert.IsType<BadRequestObjectResult>(controller.Save(new(Guid.NewGuid(), "history.ui", "completed", 1, HttpStatus: 200)));
        Assert.IsType<AcceptedResult>(controller.Save(new(Guid.NewGuid(), "history.ui", "completed", 3000, 50)));
        await recorder.StopAsync(default);
        var result = Assert.IsType<OkObjectResult>(await controller.Get(default));
        var json = JsonSerializer.Serialize(result.Value); Assert.DoesNotContain("ClientId", json); Assert.DoesNotContain("local-owner", json);
        Assert.Contains("AppVersion", json); Assert.Contains("DeviceKey", json); Assert.Single(rows.Rows);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(86_400_001)]
    public void InvalidDurationsCannotBecomeEvidence(double duration) => Assert.False(PerformanceRules.IsValid(new(Guid.NewGuid(), "history.ui", "completed", duration)));

    [Fact]
    public void UnknownOrOversizedMetadataIsRejected()
    {
        Assert.False(PerformanceRules.IsValid(new(Guid.Empty, "history.ui", "completed", 0)));
        Assert.False(PerformanceRules.IsValid(new(Guid.NewGuid(), "private/path", "completed", 1)));
        Assert.False(PerformanceRules.IsValid(new(Guid.NewGuid(), "scan.ui", "repaired", 1)));
        Assert.False(PerformanceRules.IsValid(new(Guid.NewGuid(), "scan.ui", "completed", 1, RecordCount: -1)));
        Assert.False(PerformanceRules.IsValid(new(Guid.NewGuid(), "scan.ui", "completed", 1, Engine: "token-value")));
    }
}
