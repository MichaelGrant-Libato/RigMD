using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Application.Services.Autonomy;
using RigMD.Infrastructure.Remediation.Actions;
using RigMD.Infrastructure.Remediation.Tools;
using RigMD.Infrastructure.Remediation.Tools.Diagnostic;
using RigMD.Infrastructure.Remediation.Tools.Remediation;
using Xunit;

namespace RigMD.Tests.Services.Autonomy;

public class AgentToolLayerTests
{
    private sealed class StubHardwareProviders :
        ICpuProvider,
        IMemoryProvider,
        IProcessProvider,
        IStorageProvider,
        IGpuProvider,
        IDisplayProvider,
        INetworkProvider,
        IBatteryProvider,
        IPowerProvider,
        IDeviceTypeProvider
    {
        public CpuStatsDto GetCpuStats() => new()
        {
            Name = "AMD Ryzen 7 7800X3D",
            UsagePercent = 34.5,
            FrequencyMhz = 4650,
            MaxFrequencyMhz = 5050,
            Cores = 8,
            Threads = 16,
            IsThermallyThrottling = false,
            TemperatureCelsius = 61.5
        };

        public MemoryStatsDto GetMemoryStats() => new()
        {
            TotalGb = 32,
            UsedGb = 18.4,
            UsagePercent = 57.5,
            CommittedGb = 21.2
        };

        public ProcessInsightsDto GetProcessInsights() => new()
        {
            BrowserDetected = true,
            BrowserProcessCount = 14,
            BrowserMemoryMb = 1420.5,
            BrowserHeavy = false
        };

        public string GetPrimaryStorageType() => "NVMe SSD";

        public List<StorageDriveDto> GetStorageDrives() => new()
        {
            new() { Model = "Samsung SSD 990 PRO 1TB", Type = "NVMe SSD", SizeGb = 931.5, IsFailingSmart = false }
        };

        public List<DiskVolumeDto> GetAllDisks() => new()
        {
            new() { Drive = "C:\\", FsType = "NTFS", TotalGb = 931.5, UsedGb = 512.0, UsagePercent = 55.0 }
        };

        public GpuStatsDto GetGpuStats() => new()
        {
            Name = "NVIDIA GeForce RTX 4070",
            Type = "Dedicated",
            HasGpu = true,
            HasDedicatedGpu = true,
            Driver = "32.0.15.6094",
            DriverDate = "2026-01-15",
            VramGb = 12.0,
            DedicatedMemoryGb = 12.0
        };

        public int GetConnectedDisplays() => 1;

        public List<DisplayStatsDto> GetDisplays() => new()
        {
            new() { Name = "Primary Monitor", Resolution = "2560 x 1440", RefreshRate = 165 }
        };

        public NetworkStatsDto GetNetworkStats() => new()
        {
            HasActiveAdapter = true,
            AdapterName = "Intel Wi-Fi 6E AX210",
            HasIpv4Address = true,
            HasDefaultGateway = true,
            HasDnsServers = true,
            IsWifi = true,
            WifiSignalStrength = 92
        };

        public BatteryStatsDto? GetBatteryStats() => null;

        public string GetActivePowerPlan() => "Balanced";

        public string GetDeviceType() => "Desktop";
    }

    private static IRigMdAgentToolRegistry CreateFullRegistry()
    {
        var providers = new StubHardwareProviders();
        var loggerFactory = NullLoggerFactory.Instance;

        var tools = new IRigMdAgentTool[]
        {
            new InspectCpuAndThermalsTool(providers),
            new InspectMemoryAndProcessesTool(providers, providers),
            new InspectStorageHealthTool(providers),
            new InspectGpuAndDisplaysTool(providers, providers),
            new InspectNetworkConnectivityTool(providers),
            new InspectBatteryAndPowerTool(providers, providers, providers),
            new QueryWindowsEventLogsTool(),
            new QueryStartupAppsTool(),
            new TerminateProcessesTool(loggerFactory),
            new ClearTempFilesTool(loggerFactory),
            new ClearBrowserCacheTool(loggerFactory),
            new FlushDnsCacheTool(loggerFactory),
            new RestartWindowsExplorerTool(loggerFactory),
            new ClearWindowsUpdateCacheTool(loggerFactory),
            new RunSystemFileCheckerTool(loggerFactory)
        };

        return new RigMdAgentToolRegistry(tools);
    }

    [Fact]
    public void Registry_RegistersAll15Tools_WithExpectedSafetyTiersAndDeclarations()
    {
        var registry = CreateFullRegistry();
        var allTools = registry.GetAllTools();

        Assert.Equal(15, allTools.Count);
        Assert.Equal(8, registry.GetToolsByTier(ToolSafetyTier.Tier0_ReadOnly).Count);
        Assert.Equal(3, registry.GetToolsByTier(ToolSafetyTier.Tier1_SafeReversible).Count);
        Assert.Equal(4, registry.GetToolsByTier(ToolSafetyTier.Tier2_DestructiveOrAdmin).Count);

        var readOnlyDeclarations = registry.GetFunctionDeclarations(includeWriteTools: false);
        Assert.Equal(8, readOnlyDeclarations.Count);

        var allDeclarations = registry.GetFunctionDeclarations(includeWriteTools: true);
        Assert.Equal(15, allDeclarations.Count);

        foreach (var decl in allDeclarations)
        {
            Assert.False(string.IsNullOrWhiteSpace(decl.Name));
            Assert.False(string.IsNullOrWhiteSpace(decl.Description));
        }

        // Verify aliases for prompt compatibility
        Assert.NotNull(registry.GetTool("inspect_gpu_status"));
        Assert.Equal("inspect_gpu_and_displays", registry.GetTool("inspect_gpu_status")!.Name);
        Assert.NotNull(registry.GetTool("inspect_dns"));
        Assert.Equal("inspect_network_connectivity", registry.GetTool("inspect_dns")!.Name);
    }

    [Fact]
    public void DiagnosticScopeMapper_MapsComponentsAndScenariosAccurately()
    {
        // Component filtering: selecting ONLY "Memory" must only allow inspect_memory_and_processes
        var (memoryTools, _) = DiagnosticScopeMapper.GetAllowedToolsForComponents(new[] { "Memory" });
        Assert.Single(memoryTools);
        Assert.Contains("inspect_memory_and_processes", memoryTools);
        Assert.DoesNotContain("inspect_storage_health", memoryTools);

        // Scenario mapping: supports both snake_case and kebab-case scenario IDs
        var slowBootTools = DiagnosticScopeMapper.GetRequiredToolsForScenario("slow_boot");
        Assert.Contains(slowBootTools, t => t.ToolName == "query_startup_apps");
        Assert.Contains(slowBootTools, t => t.ToolName == "query_windows_event_logs" && t.ArgumentsJson.Contains("Boot"));

        var blueScreenTools = DiagnosticScopeMapper.GetRequiredToolsForScenario("blue-screen-crash");
        Assert.Contains(blueScreenTools, t => t.ToolName == "query_windows_event_logs" && t.ArgumentsJson.Contains("BugCheck"));
        Assert.Contains(blueScreenTools, t => t.ToolName == "inspect_gpu_and_displays");
    }

    [Fact]
    public async Task Tier0DiagnosticTools_ReturnStructuredJsonObservations()
    {
        var registry = CreateFullRegistry();
        using var emptyArgs = JsonDocument.Parse("{}");

        var cpuTool = registry.GetTool("inspect_cpu_and_thermals")!;
        var cpuResult = await cpuTool.ExecuteAsync(emptyArgs.RootElement);
        Assert.True(cpuResult.Success);
        Assert.Contains("AMD Ryzen 7 7800X3D", cpuResult.DataJson);
        Assert.Contains("61.5", cpuResult.DataJson);

        var gpuTool = registry.GetTool("inspect_gpu_and_displays")!;
        var gpuResult = await gpuTool.ExecuteAsync(emptyArgs.RootElement);
        Assert.True(gpuResult.Success);
        Assert.Contains("RTX 4070", gpuResult.DataJson);

        var powerTool = registry.GetTool("inspect_battery_and_power")!;
        var powerResult = await powerTool.ExecuteAsync(emptyArgs.RootElement);
        Assert.True(powerResult.Success);
        Assert.Contains("Balanced", powerResult.DataJson);

        var startupTool = registry.GetTool("query_startup_apps")!;
        var startupResult = await startupTool.ExecuteAsync(emptyArgs.RootElement);
        Assert.True(startupResult.Success);
        Assert.Contains("startupAppCount", startupResult.DataJson);
    }

    [Theory]
    [InlineData("lsass")]
    [InlineData("svchost.exe")]
    [InlineData("csrss")]
    [InlineData("explorer")]
    [InlineData("rigmd.api")]
    [InlineData("msmpeng")]
    [InlineData("MsSense")]
    public async Task TerminateProcessesTool_Preview_StrictlyBlocksProtectedSystemProcesses(string protectedName)
    {
        var registry = CreateFullRegistry();
        var tool = registry.GetTool("terminate_processes")!;

        using var argsDoc = JsonDocument.Parse(
            JsonSerializer.Serialize(new
            {
                processNames = new[] { protectedName },
                reason = "Test protected process denylist"
            }));

        var preview = await tool.PreviewImpactAsync(argsDoc.RootElement);

        Assert.False(preview.CanExecute);
        Assert.True(TerminateProcessesAction.IsProtectedProcess(protectedName));
        Assert.Contains(preview.Warnings, w => w.Contains("protected", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RemediationTools_DryRunPreviews_ReportRealImpactWithoutMutatingState()
    {
        var registry = CreateFullRegistry();
        using var emptyArgs = JsonDocument.Parse("{}");

        var tempPreview = await registry.GetTool("clear_temp_files")!.PreviewImpactAsync(emptyArgs.RootElement);
        Assert.True(tempPreview.CanExecute);
        Assert.True(tempPreview.RequiresUserConfirmation);
        Assert.NotEmpty(tempPreview.AffectedTargets);

        var dnsPreview = await registry.GetTool("flush_dns_cache")!.PreviewImpactAsync(emptyArgs.RootElement);
        Assert.True(dnsPreview.CanExecute);
        Assert.Contains("DNS", dnsPreview.WhatWillHappen);

        var browserPreview = await registry.GetTool("clear_browser_cache")!.PreviewImpactAsync(emptyArgs.RootElement);
        Assert.True(browserPreview.CanExecute);
        Assert.Equal(3, browserPreview.AffectedTargets.Count);

        var sfcPreview = await registry.GetTool("run_system_file_checker")!.PreviewImpactAsync(emptyArgs.RootElement);
        Assert.True(sfcPreview.RequiresAdmin);
    }
}
