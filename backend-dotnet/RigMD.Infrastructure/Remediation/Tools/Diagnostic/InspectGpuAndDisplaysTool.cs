using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;
using RigMD.Infrastructure.Windows;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectGpuAndDisplaysTool : IRigMdAgentTool
{
    private readonly IGpuProvider _gpuProvider;
    private readonly IDisplayProvider _displayProvider;
    private readonly IHardwareMonitorService? _hardwareMonitor;

    public InspectGpuAndDisplaysTool(
        IGpuProvider gpuProvider,
        IDisplayProvider displayProvider,
        IHardwareMonitorService? hardwareMonitor = null)
    {
        _gpuProvider = gpuProvider;
        _displayProvider = displayProvider;
        _hardwareMonitor = hardwareMonitor;
    }

    public string Name => "inspect_gpu_and_displays";

    public string DisplayName => "Inspect GPU, VRAM & Displays";

    public string Description =>
        "Reads GPU model, driver version and date, dedicated/shared VRAM, live GPU core load and temperature, connected display resolutions/refresh rates, and PnP display adapter error codes.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>(),
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = false,
            WhatWillHappen = "Reads GPU driver metadata, VRAM sensors, and connected display configurations."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Inspecting GPU telemetry, VRAM usage, and connected displays...");

        var gpu = _gpuProvider.GetGpuStats();
        var displays = _displayProvider.GetDisplays();

        double? gpuLoadPercent = null;
        double? vramUsedGb = null;
        double? vramTotalGb = null;

        if (_hardwareMonitor != null)
        {
            try
            {
                _hardwareMonitor.Tick();
                gpu.TemperatureCelsius ??= _hardwareMonitor.GetGpuTemperature();
                gpuLoadPercent = _hardwareMonitor.GetGpuLoad();
                vramUsedGb = _hardwareMonitor.GetGpuDedicatedMemoryUsedGb();
                vramTotalGb = _hardwareMonitor.GetGpuDedicatedMemoryTotalGb();
            }
            catch
            {
                // Ignore LHM sensor errors
            }
        }

        var payload = new
        {
            gpu = new
            {
                name = gpu.Name,
                type = gpu.Type,
                driverVersion = gpu.Driver,
                driverDate = gpu.DriverDate,
                directXVersion = gpu.DirectXVersion,
                vramGb = Math.Round(gpu.VramGb, 2),
                dedicatedMemoryGb = Math.Round(vramTotalGb ?? gpu.DedicatedMemoryGb, 2),
                dedicatedMemoryUsedGb = vramUsedGb.HasValue ? Math.Round(vramUsedGb.Value, 2) : (double?)null,
                sharedMemoryGb = Math.Round(gpu.SharedMemoryGb, 2),
                coreLoadPercent = gpuLoadPercent.HasValue ? Math.Round(gpuLoadPercent.Value, 1) : (double?)null,
                temperatureCelsius = gpu.TemperatureCelsius
            },
            connectedDisplaysCount = displays.Count,
            displays
        };

        var tempStr = gpu.TemperatureCelsius.HasValue ? $"{gpu.TemperatureCelsius.Value:F1}°C" : "N/A";
        var summary =
            $"GPU '{gpu.Name}' ({gpu.Type}, Driver {gpu.Driver} [{gpu.DriverDate}]), VRAM: {gpu.VramGb:F1} GB, Temp: {tempStr}, Connected Displays: {displays.Count}.";

        return Task.FromResult(new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        });
    }
}
