using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using RigMD.Api.Controllers;
using RigMD.Api.Middleware;
using RigMD.Api.Services;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Domain.Entities;
using RigMD.Infrastructure.Remediation.Tools;

namespace RigMD.Tests.Api;

public class LocalApiSecurityTests
{
    private const string Token = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static DefaultHttpContext Request(string path = "/api/profile", string? token = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Response.Body = new MemoryStream();
        if (token != null) context.Request.Headers.Authorization = $"Bearer {token}";
        return context;
    }

    private static async Task<bool> Authenticate(DefaultHttpContext context, string? configuredToken = Token)
    {
        var called = false;
        var middleware = new LocalApiAuthenticationMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, new LocalApiSession(configuredToken));
        return called;
    }

    [Theory]
    [InlineData("/api/profile")]
    [InlineData("/hubs/telemetry")]
    [InlineData("/hubs/remediation/negotiate")]
    public async Task MissingCredential_IsRejected_EvenWithSpoofedClientId(string path)
    {
        var context = Request(path);
        context.Request.Headers["X-Client-ID"] = "victim";
        Assert.False(await Authenticate(context));
        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    public async Task MissingOrInvalidServerCredential_FailsClosed(string? configuredToken)
    {
        var context = Request(token: Token);
        Assert.False(await Authenticate(context, configuredToken));
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:5273")]
    [InlineData("http://127.0.0.1:5273")]
    [InlineData("http://localhost:5173")]
    public async Task ValidCredential_UsesTrustedInstallationIdentity(string? origin)
    {
        var context = Request(token: Token);
        context.Request.Headers["X-Client-ID"] = "victim";
        if (origin != null) context.Request.Headers.Origin = origin;

        Assert.True(await Authenticate(context));
        Assert.True(context.User.Identity!.IsAuthenticated);
        var provider = new HttpCurrentClientProvider(new HttpContextAccessor { HttpContext = context });
        Assert.Equal("local", provider.GetCurrentClientId());
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:5273.evil.example")]
    [InlineData("null")]
    public async Task ForeignOrigin_IsRejected_EvenWithValidCredential(string origin)
    {
        var context = Request(token: Token);
        context.Request.Headers.Origin = origin;
        Assert.False(await Authenticate(context));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task NonLoopbackPeer_IsRejected()
    {
        var context = Request(token: Token);
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        Assert.False(await Authenticate(context));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task CrossSiteFetch_IsRejected()
    {
        var context = Request(token: Token);
        context.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Assert.False(await Authenticate(context));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("/hubs/telemetry", "GET", true)]
    [InlineData("/hubs/remediation", "GET", true)]
    [InlineData("/hubs/remediation/negotiate", "POST", false)]
    [InlineData("/api/profile", "GET", false)]
    public async Task QueryCredential_IsLimitedToHubGetRequests(string path, string method, bool permitted)
    {
        var context = Request(path);
        context.Request.Method = method;
        context.Request.QueryString = new QueryString($"?access_token={Token}");
        Assert.Equal(permitted, await Authenticate(context));
        if (!permitted) Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvalidAuthorization_CannotFallBackToQueryCredential()
    {
        var context = Request("/hubs/telemetry");
        context.Request.Headers.Authorization = "Basic invalid";
        context.Request.QueryString = new QueryString($"?access_token={Token}");
        Assert.False(await Authenticate(context));
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task DuplicateAuthorization_IsRejected()
    {
        var context = Request();
        context.Request.Headers.Authorization = new StringValues(new[] { $"Bearer {Token}", $"Bearer {Token}" });
        Assert.False(await Authenticate(context));
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task RotatedCredential_RejectsPreviousLaunch()
    {
        var context = Request(token: Token);
        Assert.False(await Authenticate(context, new string('B', 64)));
        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task StaticFrontend_DoesNotRequireCredential()
    {
        Assert.True(await Authenticate(Request("/assets/app.js"), configuredToken: null));
    }

    [Fact]
    public void ClientProvider_WithoutAuthenticatedIdentity_FailsClosed()
    {
        var accessor = new HttpContextAccessor();
        var provider = new HttpCurrentClientProvider(accessor);
        Assert.Throws<UnauthorizedAccessException>(() => provider.GetCurrentClientId());
        accessor.HttpContext = Request();
        accessor.HttpContext.Items["ClientId"] = "victim";
        accessor.HttpContext.Request.Headers["X-Client-ID"] = "victim";
        Assert.Throws<UnauthorizedAccessException>(() => provider.GetCurrentClientId());
        accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));
        Assert.Throws<UnauthorizedAccessException>(() => provider.GetCurrentClientId());
    }

    [Fact]
    public void ExecutionGate_RejectsOverlap_AndReleasesAfterDisposal()
    {
        var gate = new LocalRemediationExecutionGate();
        var first = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());
        Assert.Null(gate.TryEnter());
        first.Dispose();
        using var second = Assert.IsAssignableFrom<IDisposable>(gate.TryEnter());
        first.Dispose();
        Assert.Null(gate.TryEnter());
    }

    [Theory]
    [InlineData(ToolSafetyTier.Tier1_SafeReversible)]
    [InlineData(ToolSafetyTier.Tier2_DestructiveOrAdmin)]
    public async Task DirectToolExecution_BlocksAllWriteTiers(ToolSafetyTier tier)
    {
        var tool = new FakeTool(tier);
        var controller = Controller(tool);
        using var args = JsonDocument.Parse("{}");
        var result = await controller.ExecuteTool(new AutonomyController.ToolInvocationRequest
        {
            ToolName = tool.Name, Arguments = args.RootElement
        }, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(0, tool.ExecutionCount);
    }

    [Fact]
    public async Task DirectToolExecution_AllowsReadOnlyDiagnostics()
    {
        var tool = new FakeTool(ToolSafetyTier.Tier0_ReadOnly);
        using var args = JsonDocument.Parse("{}");
        var result = await Controller(tool).ExecuteTool(new AutonomyController.ToolInvocationRequest
        {
            ToolName = tool.Name, Arguments = args.RootElement
        }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, tool.ExecutionCount);
    }

    [Fact]
    public async Task Execution_RechecksSafetyAgainstActualArguments_BeforeChangingSystem()
    {
        var tool = new FakeTool(ToolSafetyTier.Tier1_SafeReversible);
        var orchestrator = new AutonomousOrchestrator(null!, new RigMdAgentToolRegistry(new[] { tool }));
        var result = await orchestrator.RunExecutionCycleAsync(new DiagnosticOutput
        {
            DiagnosedCategory = "Storage & Temp Cache Pressure"
        }, new HardwareProfileDto(), userConsentProvided: true,
            requestedToolName: tool.Name, requestedArgumentsJson: "{\"target\":\"changed\"}");
        Assert.False(result.Safety!.IsApproved);
        Assert.Equal(RemediationAttemptState.SafetyRejected, Assert.Single(result.Attempts).State);
        Assert.Equal("changed", tool.PreviewedTarget);
        Assert.Equal(0, tool.ExecutionCount);
    }

    [Fact]
    public async Task Execute_WithoutConsent_IsRejectedBeforeAccessingSystem()
    {
        var result = await Controller().Execute(new AutonomyController.ExecuteRequest
        {
            UserConsentProvided = false, SessionId = Guid.NewGuid().ToString()
        });
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Execute_WhenAnotherFixIsRunning_ReturnsConflictBeforeAccessingSystem()
    {
        var gate = new LocalRemediationExecutionGate();
        using var lease = gate.TryEnter();
        var result = await Controller(gate: gate).Execute(new AutonomyController.ExecuteRequest
        {
            UserConsentProvided = true, SessionId = Guid.NewGuid().ToString()
        });
        Assert.IsType<ConflictObjectResult>(result);
    }

    private static AutonomyController Controller(FakeTool? tool = null, LocalRemediationExecutionGate? gate = null) =>
        new(null!, null!, null!, null!, null!, NullLogger<AutonomyController>.Instance,
            tool == null ? null : new RigMdAgentToolRegistry(new[] { tool }), executionGate: gate);

    internal sealed class FakeTool(ToolSafetyTier tier) : IRigMdAgentTool
    {
        public string Name => "clear_temp_files";
        public string DisplayName => "Fake remediation";
        public string Description => "Test double; never changes the system.";
        public ToolSafetyTier SafetyTier => tier;
        public int ExecutionCount { get; private set; }
        public string? PreviewedTarget { get; private set; }
        public bool CanPreviewExecute { get; init; }
        public AgentToolFunctionDeclaration GetFunctionDeclaration() => new() { Name = Name, Description = Description };
        public Task<ToolDryRunPreview> PreviewImpactAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            PreviewedTarget = arguments.TryGetProperty("target", out var target) ? target.GetString() : null;
            return Task.FromResult(new ToolDryRunPreview { CanExecute = CanPreviewExecute, WhatWillHappen = "Current test conditions." });
        }
        public Task<AgentToolExecutionResult> ExecuteAsync(JsonElement arguments, Action<string>? progressReporter = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return Task.FromResult(new AgentToolExecutionResult { Success = true });
        }
    }
}
