using RigMD.Infrastructure;
using RigMD.Application.Services;
using RigMD.Application.Contracts.Providers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddControllers();

// Add CORS policy to allow the React frontend on port 5273
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins("http://localhost:5273", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Dependency Injection
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.ICpuProvider, RigMD.Infrastructure.Windows.WmiCpuProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IGpuProvider, RigMD.Infrastructure.Windows.WmiGpuProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IMemoryProvider, RigMD.Infrastructure.Windows.WmiMemoryProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IOperatingSystemProvider, RigMD.Infrastructure.Windows.WmiOperatingSystemProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IStorageProvider, RigMD.Infrastructure.Windows.WmiStorageProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IMotherboardProvider, RigMD.Infrastructure.Windows.WmiMotherboardProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IProcessProvider, RigMD.Infrastructure.Windows.ProcessProvider>();
builder.Services.AddScoped<RigMD.Application.Contracts.Providers.IWindowsSystemProfileService, RigMD.Infrastructure.Windows.WindowsSystemProfileService>();
builder.Services.AddScoped<RigMD.Application.Contracts.Ai.IAiExplainer, RigMD.Infrastructure.Ai.OfflineAiExplainer>();
builder.Services.AddScoped<RigMD.Application.Services.IDiagnosticEngineService, RigMD.Application.Services.DiagnosticEngineService>();
builder.Services.AddScoped<RigMD.Infrastructure.DatabaseSessionService>();
builder.Services.AddScoped<DatabaseSessionService>();
builder.Services.AddScoped<ResolutionService>();
builder.Services.AddScoped<RecurringPatternService>();
builder.Services.AddScoped<WarningSignService>();

// Autonomy
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationRegistry, RigMD.Application.Services.Autonomy.RemediationRegistry>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationPlanner, RigMD.Application.Services.Autonomy.RemediationPlanner>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.ISafetyPolicy, RigMD.Application.Services.Autonomy.SafetyPolicy>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IRemediationExecutor, RigMD.Infrastructure.Remediation.WindowsRemediationExecutor>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IVerificationService, RigMD.Infrastructure.Remediation.VerificationService>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IAutonomousOrchestrator, RigMD.Application.Services.Autonomy.AutonomousOrchestrator>();
builder.Services.AddScoped<RigMD.Application.Contracts.Autonomy.IDryRunRemediationExecutor,RigMD.Application.Services.Autonomy.DryRunRemediationExecutor>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReact");

app.UseHttpsRedirection();

app.MapControllers();

app.Run();
