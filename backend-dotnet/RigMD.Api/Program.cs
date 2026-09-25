using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Services;
using RigMD.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Load optional local developer overrides (gitignored and excluded from publish/installer)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

// Add CORS policy to allow the React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins("http://localhost:5273", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ---------------------------------------------------------------
// SQLite + EF Core
// ---------------------------------------------------------------
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "RigMD",
    "rigmd.db");

Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<RigMdDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Client identification & isolation
builder.Services.AddScoped<RigMD.Application.Contracts.Common.ICurrentClientProvider, RigMD.Api.Services.HttpCurrentClientProvider>();

// Repository abstractions
builder.Services.AddScoped<
    IDiagnosticSessionRepository,
    DiagnosticSessionRepository>();

builder.Services.AddScoped<
    IRemediationRepository,
    RemediationRepository>();

builder.Services.AddScoped<
    IAutomaticDiagnosisService,
    AutomaticDiagnosisService>();

builder.Services.AddScoped<
    RigMD.Application.Contracts.Providers.INetworkProvider,
    RigMD.Infrastructure.Windows.WindowsNetworkProvider>();

// ---------------------------------------------------------------
// Windows Observation Layer
// ---------------------------------------------------------------
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.ICpuProvider,              RigMD.Infrastructure.Windows.WmiCpuProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IGpuProvider,              RigMD.Infrastructure.Windows.WmiGpuProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IMemoryProvider,           RigMD.Infrastructure.Windows.WmiMemoryProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IOperatingSystemProvider,  RigMD.Infrastructure.Windows.WmiOperatingSystemProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IStorageProvider,          RigMD.Infrastructure.Windows.WmiStorageProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IMotherboardProvider,      RigMD.Infrastructure.Windows.WmiMotherboardProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IProcessProvider,          RigMD.Infrastructure.Windows.ProcessProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IBatteryProvider,          RigMD.Infrastructure.Windows.WmiBatteryProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IDeviceTypeProvider,       RigMD.Infrastructure.Windows.WmiDeviceTypeProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IPowerProvider,            RigMD.Infrastructure.Windows.WmiPowerProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IDisplayProvider,          RigMD.Infrastructure.Windows.WmiDisplayProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IWindowsSystemProfileService, RigMD.Infrastructure.Windows.WindowsSystemProfileService>();
builder.Services.AddSingleton<RigMD.Infrastructure.Windows.IHardwareMonitorService, RigMD.Infrastructure.Windows.HardwareMonitorService>();

// ---------------------------------------------------------------
// Application Services
// ---------------------------------------------------------------
builder.Services.AddScoped<RigMD.Infrastructure.Ai.OfflineAiExplainer>();
builder.Services.AddScoped<RigMD.Application.Contracts.Ai.IAiExplainer, RigMD.Infrastructure.Ai.GeminiAiExplainer>();
builder.Services.AddScoped<RigMD.Application.Services.IDiagnosticEngineService,      RigMD.Application.Services.DiagnosticEngineService>();
builder.Services.AddScoped<ResolutionService>();
builder.Services.AddScoped<RecurringPatternService>();
builder.Services.AddScoped<WarningSignService>();

// ---------------------------------------------------------------
// Autonomy & Agent Tooling Layer (Phase 2)
// ---------------------------------------------------------------
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationExecutor,    RigMD.Infrastructure.Remediation.WindowsRemediationExecutor>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IAutonomousOrchestrator, RigMD.Application.Services.Autonomy.AutonomousOrchestrator>();

// Tier 0: Read-Only Diagnostic Tools
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectCpuAndThermalsTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectMemoryAndProcessesTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectStorageHealthTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectGpuAndDisplaysTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectNetworkConnectivityTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.InspectBatteryAndPowerTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.QueryWindowsEventLogsTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Diagnostic.QueryStartupAppsTool>();

// Tier 1 & Tier 2: OS Remediation Tools
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.TerminateProcessesTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.ClearTempFilesTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.ClearBrowserCacheTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.FlushDnsCacheTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.RestartWindowsExplorerTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.ClearWindowsUpdateCacheTool>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentTool, RigMD.Infrastructure.Remediation.Tools.Remediation.RunSystemFileCheckerTool>();

builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRigMdAgentToolRegistry, RigMD.Infrastructure.Remediation.Tools.RigMdAgentToolRegistry>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IReActLlmClient, RigMD.Infrastructure.Ai.GeminiReActLlmClient>();

builder.Services.AddHostedService<RigMD.Api.Services.TelemetryBackgroundService>();

builder.Services.AddScoped<DatabaseSyncService>();
builder.Services.AddScoped<LocalDatabaseSchemaUpgradeService>();

var app = builder.Build();

// Auto-create SQLite schema and synchronize with Supabase PostgreSQL on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RigMdDbContext>();
    db.Database.EnsureCreated();

    var schemaUpgradeService =
        scope.ServiceProvider.GetRequiredService<LocalDatabaseSchemaUpgradeService>();
    await schemaUpgradeService.ApplyAsync();

    var syncService =
        scope.ServiceProvider.GetRequiredService<DatabaseSyncService>();
    await syncService.SyncFromPostgresAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReact");
app.UseHttpsRedirection();

// ---------------------------------------------------------------
// Serve React Frontend
// ---------------------------------------------------------------
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});

app.UseMiddleware<RigMD.Api.Middleware.ClientIdMiddleware>();
app.MapControllers();
app.MapHub<RigMD.Api.Hubs.RemediationHub>("/hubs/remediation");
app.MapHub<RigMD.Api.Hubs.TelemetryHub>("/hubs/telemetry");
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
        ctx.Context.Response.Headers["Expires"] = "0";
    }
});

app.Run();

public partial class Program { }
