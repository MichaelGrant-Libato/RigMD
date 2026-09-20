using RigMD.Agent;
using RigMD.Agent.Services;
using RigMD.Agent.Tools;
using RigMD.Application.Contracts.Providers;
using RigMD.Infrastructure.Windows;
using RigMD.Infrastructure.Remediation.Actions;
using System.IO;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "RigMD Agent";
});

builder.Services.AddSingleton<
    AgentIdentityService>();

builder.Services.AddSingleton<
    InteractiveUserTempPathResolver>();

builder.Services.AddScoped<
    ICpuProvider,
    WmiCpuProvider>();

builder.Services.AddScoped<
    IGpuProvider,
    WmiGpuProvider>();

builder.Services.AddScoped<
    IMemoryProvider,
    WmiMemoryProvider>();

builder.Services.AddScoped<
    IOperatingSystemProvider,
    WmiOperatingSystemProvider>();

builder.Services.AddScoped<
    IStorageProvider,
    WmiStorageProvider>();

builder.Services.AddScoped<
    IMotherboardProvider,
    WmiMotherboardProvider>();

builder.Services.AddScoped<
    IProcessProvider,
    ProcessProvider>();

builder.Services.AddScoped<
    IBatteryProvider,
    WmiBatteryProvider>();

builder.Services.AddScoped<
    IDeviceTypeProvider,
    WmiDeviceTypeProvider>();

builder.Services.AddScoped<
    IPowerProvider,
    WmiPowerProvider>();

builder.Services.AddScoped<
    IDisplayProvider,
    WmiDisplayProvider>();

builder.Services.AddScoped<
    IWindowsSystemProfileService,
    WindowsSystemProfileService>();

builder.Services.AddSingleton<
    RigMD.Infrastructure.Windows.IHardwareMonitorService,
    RigMD.Infrastructure.Windows.HardwareMonitorService>();

builder.Services.AddScoped<
    IAgentTool,
    CpuScanTool>();

builder.Services.AddScoped<
    IAgentTool,
    MemoryScanTool>();

builder.Services.AddScoped<
    IAgentTool,
    GpuScanTool>();

builder.Services.AddScoped<
    IAgentTool,
    StorageScanTool>();

builder.Services.AddScoped<
    IAgentTool,
    ProcessScanTool>();

builder.Services.AddScoped<
    IAgentTool,
    SystemProfileScanTool>();

builder.Services.AddScoped<
    AgentToolRegistry>();

builder.Services.AddScoped<
    ClearTempFilesAction>();

builder.Services.AddScoped<
    FlushDnsAction>();

builder.Services.AddHostedService<
    Worker>();

builder.Services.AddScoped<
    INetworkProvider,
    WindowsNetworkProvider>();

var apiBaseUrl =
    builder.Configuration["Agent:ApiBaseUrl"];

if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    throw new InvalidOperationException(
        "Agent:ApiBaseUrl is not configured.");
}

builder.Services.AddHttpClient<
    AgentApiClient>(client =>
{
    client.BaseAddress =
        new Uri(apiBaseUrl);
});

var host = builder.Build();

host.Run();