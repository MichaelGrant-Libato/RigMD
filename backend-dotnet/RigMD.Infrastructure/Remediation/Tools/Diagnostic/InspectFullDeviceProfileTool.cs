using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectFullDeviceProfileTool : IRigMdAgentTool
{
    private readonly IWindowsSystemProfileService _profileService;

    public InspectFullDeviceProfileTool(IWindowsSystemProfileService profileService)
    {
        _profileService = profileService;
    }

    public string Name => "inspect_full_device_profile";

    public string DisplayName => "Inspect Full Device Info & Hardware Profile";

    public string Description =>
        "Reads the complete Windows hardware and OS profile ('My Device Info'), including device name, chassis type (Desktop/Laptop), OS build, motherboard/chipset, active power plan, CPU specs, GPU specs & driver, RAM sticks & slots, physical storage drives & S.M.A.R.T. health, battery presence, connected displays, network adapter, and Device Manager errors.";

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
            WhatWillHappen = "Reads the full Windows hardware and OS device profile without modifying system state."
        });
    }

    public Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        progressReporter?.Invoke("Reading full Windows hardware & OS profile (My Device Info)...");

        var profile = _profileService.GetLiveSystemProfile();
        var presence = _profileService.GetHardwarePresenceProbe();

        var driveSummaries = (profile.StorageDrives ?? new List<StorageDriveDto>())
            .Select(d => new
            {
                model = d.Model,
                type = d.Type,
                sizeGb = Math.Round(d.SizeGb, 1),
                usedGb = d.UsedGb.HasValue ? Math.Round(d.UsedGb.Value, 1) : (double?)null,
                usagePercent = d.UsagePercent.HasValue ? Math.Round(d.UsagePercent.Value, 1) : (double?)null,
                isFailingSmart = d.IsFailingSmart
            })
            .ToList();

        var payload = new
        {
            deviceName = profile.DeviceName,
            deviceType = profile.DeviceType,
            osVersion = profile.OsVersion,
            systemAge = profile.SystemAge,
            chipsetDriver = profile.ChipsetDriver,
            activePowerPlan = profile.ActivePowerPlan,
            primaryStorageType = profile.PrimaryStorageType,
            hardwarePresence = new
            {
                hasBattery = presence.HasBattery,
                hasGpu = presence.HasGpu,
                hasDedicatedGpu = presence.HasDedicatedGpu
            },
            cpu = new
            {
                name = profile.Cpu.Name,
                cores = profile.Cpu.Cores,
                threads = profile.Cpu.Threads,
                usagePercent = Math.Round(profile.Cpu.UsagePercent, 1),
                frequencyMhz = Math.Round(profile.Cpu.FrequencyMhz, 0),
                temperatureCelsius = profile.Cpu.TemperatureCelsius
            },
            gpu = new
            {
                name = profile.Gpu.Name,
                type = profile.Gpu.Type,
                vramGb = Math.Round(profile.Gpu.VramGb, 1),
                driver = profile.Gpu.Driver,
                temperatureCelsius = profile.Gpu.TemperatureCelsius
            },
            ram = new
            {
                totalGb = Math.Round(profile.Ram.TotalGb, 1),
                usedGb = Math.Round(profile.Ram.UsedGb, 1),
                usagePercent = Math.Round(profile.Ram.UsagePercent, 1),
                speedMtps = profile.Ram.SpeedMtps,
                slotsUsed = profile.Ram.SlotsUsed,
                slotsTotal = profile.Ram.SlotsTotal
            },
            storageDrives = driveSummaries,
            battery = profile.Battery,
            connectedDisplays = profile.ConnectedDisplays,
            deviceErrorsCount = profile.DeviceErrors?.Count ?? 0,
            deviceErrors = profile.DeviceErrors?.Take(5).ToList()
        };

        var summary =
            $"Device: {profile.DeviceName} ({profile.DeviceType}, {profile.OsVersion}) | " +
            $"CPU: {profile.Cpu.Name} ({profile.Cpu.UsagePercent:F1}%, {(profile.Cpu.TemperatureCelsius.HasValue ? $"{profile.Cpu.TemperatureCelsius.Value:F0}°C" : "N/A")}) | " +
            $"RAM: {profile.Ram.UsedGb:F1}/{profile.Ram.TotalGb:F1} GB ({profile.Ram.UsagePercent:F1}%) | " +
            $"GPU: {profile.Gpu.Name} | " +
            $"Drives: {driveSummaries.Count} ({profile.PrimaryStorageType}) | " +
            $"Device Manager Errors: {profile.DeviceErrors?.Count ?? 0}.";

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
