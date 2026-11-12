using Microsoft.Extensions.DependencyInjection;
using RigMD.Application.Contracts.Autonomy;

namespace RigMD.Infrastructure.Remediation.Tools;

public static class AgentToolRegistration
{
    public static IServiceCollection AddRigMdAgentTools(this IServiceCollection services)
    {
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectCpuAndThermalsTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectMemoryAndProcessesTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectStorageHealthTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectGpuAndDisplaysTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectNetworkConnectivityTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectBatteryAndPowerTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.QueryWindowsEventLogsTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.QueryStartupAppsTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.InspectFullDeviceProfileTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.QueryPastChecksAndRemediationsTool>();
        services.AddScoped<IRigMdAgentTool, Diagnostic.QueryRecurringProblemsAndWarningsTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.TerminateProcessesTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.ClearTempFilesTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.ClearBrowserCacheTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.FlushDnsCacheTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.RestartWindowsExplorerTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.ClearWindowsUpdateCacheTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.RunSystemFileCheckerTool>();
        services.AddScoped<IRigMdAgentTool, Remediation.RescanPlugAndPlayDevicesTool>();
        services.AddScoped<IRigMdAgentToolRegistry, RigMdAgentToolRegistry>();
        return services;
    }
}
