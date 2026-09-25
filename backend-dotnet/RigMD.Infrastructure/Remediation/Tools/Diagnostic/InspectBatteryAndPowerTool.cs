using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectBatteryAndPowerTool : IRigMdAgentTool
{
    private readonly IBatteryProvider _batteryProvider;
    private readonly IPowerProvider _powerProvider;
    private readonly IDeviceTypeProvider _deviceTypeProvider;

    public InspectBatteryAndPowerTool(
        IBatteryProvider batteryProvider,
        IPowerProvider powerProvider,
        IDeviceTypeProvider deviceTypeProvider)
    {
        _batteryProvider = batteryProvider;
        _powerProvider = powerProvider;
        _deviceTypeProvider = deviceTypeProvider;
    }

    public string Name => "inspect_battery_and_power";

    public string DisplayName => "Inspect Battery & Power Plan";

    public string Description =>
        "Reads device form factor (Desktop vs. Laptop), active Windows power plan (Balanced, High Performance, Power Saver), and battery charge/charging/health status if a battery is present.";

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
            WhatWillHappen = "Reads Windows active power scheme and battery telemetry without modifying settings."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Inspecting active Windows power plan and battery telemetry...");

        var deviceType = _deviceTypeProvider.GetDeviceType();
        var activePowerPlan = _powerProvider.GetActivePowerPlan();
        var battery = _batteryProvider.GetBatteryStats();

        var payload = new
        {
            deviceType,
            activePowerPlan,
            hasBattery = battery?.HasBattery ?? false,
            battery = battery != null && battery.HasBattery
                ? new
                {
                    chargePercent = battery.ChargePercent,
                    isCharging = battery.IsCharging,
                    statusDescription = battery.StatusDescription,
                    healthStatus = battery.HealthStatus,
                    estimatedRunTimeMinutes = battery.EstimatedRunTime
                }
                : null
        };

        var batterySummary = battery != null && battery.HasBattery
            ? $"Battery: {battery.ChargePercent}% ({battery.StatusDescription}, Health: {battery.HealthStatus})"
            : "No battery detected (AC Desktop)";

        var summary = $"Device: {deviceType}, Active Power Plan: '{activePowerPlan}', {batterySummary}.";

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
