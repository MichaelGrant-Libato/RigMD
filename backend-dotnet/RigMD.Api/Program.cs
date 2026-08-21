using Microsoft.EntityFrameworkCore;
using RigMD.Application.Contracts.Persistence;
using RigMD.Application.Services;
using RigMD.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// Add CORS policy to allow the React frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins("http://localhost:5273", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
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

// Repository abstractions
builder.Services.AddScoped<IDiagnosticSessionRepository, DiagnosticSessionRepository>();
builder.Services.AddScoped<IRemediationRepository, RemediationRepository>();

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
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IWindowsSystemProfileService, RigMD.Infrastructure.Windows.WindowsSystemProfileService>();

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
// Autonomy
// ---------------------------------------------------------------
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationRegistry,    RigMD.Application.Services.Autonomy.RemediationRegistry>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationPlanner,     RigMD.Application.Services.Autonomy.RemediationPlanner>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IPivotEngine,            RigMD.Application.Services.Autonomy.PivotEngine>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.ISafetyPolicy,           RigMD.Application.Services.Autonomy.SafetyPolicy>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationExecutor,    RigMD.Infrastructure.Remediation.WindowsRemediationExecutor>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IVerificationService,    RigMD.Infrastructure.Remediation.VerificationService>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IAutonomousOrchestrator, RigMD.Application.Services.Autonomy.AutonomousOrchestrator>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IDryRunRemediationExecutor, RigMD.Application.Services.Autonomy.DryRunRemediationExecutor>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRollbackManager,        RigMD.Infrastructure.Remediation.RollbackManager>();

var app = builder.Build();

// Auto-create SQLite schema on startup (no manual migration step required)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RigMdDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReact");
app.UseHttpsRedirection();
app.UseMiddleware<RigMD.Api.Middleware.ClientIdMiddleware>();
app.MapControllers();
app.Run();
