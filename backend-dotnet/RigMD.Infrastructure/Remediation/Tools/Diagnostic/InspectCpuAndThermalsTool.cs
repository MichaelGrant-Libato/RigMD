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

public class InspectCpuAndThermalsTool : IRigMdAgentTool
{
    private readonly ICpuProvider _cpuProvider;
    private readonly IHardwareMonitorService? _hardwareMonitor;

    public InspectCpuAndThermalsTool(
        ICpuProvider cpuProvider,
        IHardwareMonitorService? hardwareMonitor = null)
    {
        _cpuProvider = cpuProvider;
        _hardwareMonitor = hardwareMonitor;
    }

    public string Name => "inspect_cpu_and_thermals";

    public string DisplayName => "Inspect CPU & Thermal Sensors";

    public string Description =>
        "Reads CPU utilization, clock frequency, core/thread count, reported thermal throttling, and identified CPU temperature sensors where available. Missing CPU temperature is not estimated.";

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
            WhatWillHappen = "Reads live CPU utilization, clock speeds, and thermal sensors without modifying system state."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Sampling live CPU load, clock speeds, and thermal sensors...");

        var cpu = _cpuProvider.GetCpuStats();
        double? lhmCpuLoad = null;

        if (_hardwareMonitor != null)
        {
            try
            {
                _hardwareMonitor.Tick();
                cpu.SetTemperatureReading(_hardwareMonitor.GetCpuTemperatureReading());
                lhmCpuLoad = _hardwareMonitor.GetCpuLoad();
            }
            catch
            {
                // Ignore sensor driver exceptions on restricted systems
            }
        }

        var payload = new
        {
            model = cpu.Name,
            capturedAtUtc = DateTimeOffset.UtcNow,
            usagePercent = cpu.UsageAvailable != false && double.IsFinite(cpu.UsagePercent) && cpu.UsagePercent is >= 0 and <= 100
                ? (double?)cpu.UsagePercent : null,
            sensorLoadPercent = lhmCpuLoad.HasValue ? Math.Round(lhmCpuLoad.Value, 1) : (double?)null,
            temperatureCelsius = cpu.TemperatureCelsius,
            temperatureSource = cpu.TemperatureSource,
            temperatureSensorIdentifier = cpu.TemperatureSensorIdentifier,
            temperatureCapturedAtUtc = cpu.TemperatureCapturedAtUtc,
            isThermallyThrottling = cpu.IsThermallyThrottling,
            frequencyMhz = Math.Round(cpu.FrequencyMhz, 0),
            maxFrequencyMhz = Math.Round(cpu.MaxFrequencyMhz, 0),
            cores = cpu.Cores,
            threads = cpu.Threads,
            processCount = cpu.Processes,
            handleCount = cpu.Handles,
            virtualizationEnabled = cpu.VirtualizationEnabled
        };

        var tempSummary = cpu.TemperatureCelsius.HasValue
            ? $"{cpu.TemperatureCelsius.Value:F1}°C"
            : "CPU sensor unavailable; temperature is not estimated";

        var loadSummary = cpu.UsageAvailable != false && double.IsFinite(cpu.UsagePercent) && cpu.UsagePercent is >= 0 and <= 100
            ? $"{cpu.UsagePercent:F1}% load" : "activity unavailable";
        var summary =
            $"CPU '{cpu.Name}' at {loadSummary} ({cpu.FrequencyMhz:F0}/{cpu.MaxFrequencyMhz:F0} MHz), Temp: {tempSummary}, Source: {cpu.TemperatureSource}, Thermal throttling reported: {cpu.IsThermallyThrottling}.";

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
