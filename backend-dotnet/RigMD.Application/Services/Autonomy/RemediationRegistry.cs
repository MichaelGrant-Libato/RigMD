using System;
using System.Collections.Generic;
using System.Linq;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Models;

namespace RigMD.Application.Services.Autonomy;

public class RemediationRegistry : IRemediationRegistry
{
    private readonly List<RemediationActionDef> _actions = new()
    {
        new RemediationActionDef
        {
            Id = "clear_user_temp_files",
            Name = "Clear User Temp Files",
            SupportedDiagnosisCategories = new List<string>
            {
                "Low Available Storage Space",
                "Elevated Storage Utilization",
                "Severe System Resource Exhaustion"
            },
            RiskLevel = "Low",
            IsReversible = false
        },
        new RemediationActionDef
        {
            Id = "restart_explorer",
            Name = "Restart Windows Explorer",
            SupportedDiagnosisCategories = new List<string>
            {
                "OS performance degradation",
                "Severe System Resource Exhaustion"
            },
            RiskLevel = "Low",
            IsReversible = false
        },
        new RemediationActionDef
        {
            Id = "flush_dns",
            Name = "Flush DNS Cache",
            SupportedDiagnosisCategories = new List<string>
            {
                "Network issue"
            },
            RiskLevel = "Low",
            IsReversible = false
        },
        new RemediationActionDef
        {
            Id = "clear_browser_cache",
            Name = "Clear Browser Cache",
            Description = "Clears cached data from Chrome, Edge, and Firefox. Does not affect bookmarks, passwords, or history.",
            SupportedDiagnosisCategories = new List<string>
            {
                "Low Available Storage Space",
                "Elevated Storage Utilization",
                "Severe System Resource Exhaustion"
            },
            RiskLevel = "Low",
            IsReversible = false
        },
        new RemediationActionDef
        {
            Id = "clear_windows_update_cache",
            Name = "Clear Windows Update Cache",
            Description = "Clears downloaded Windows Update files from the SoftwareDistribution folder.",
            SupportedDiagnosisCategories = new List<string>
            {
                "Low Available Storage Space",
                "Elevated Storage Utilization",
                "Severe System Resource Exhaustion"
            },
            RiskLevel = "Low",
            IsReversible = false,
            RequiresUserConfirmation = true
        },
        new RemediationActionDef
        {
            Id = "run_disk_cleanup",
            Name = "Run Windows Disk Cleanup",
            Description = "Runs the built-in Windows Disk Cleanup utility to remove system-safe temporary files.",
            SupportedDiagnosisCategories = new List<string>
            {
                "Low Available Storage Space",
                "Elevated Storage Utilization",
                "Severe System Resource Exhaustion"
            },
            RiskLevel = "Low",
            IsReversible = false
        },
        new RemediationActionDef
        {
            Id = "run_sfc_scan",
            Name = "Run System File Checker",
            Description = "Scans and repairs corrupted Windows system files using sfc /scannow.",
            SupportedDiagnosisCategories = new List<string>
            {
                "OS performance degradation"
            },
            RiskLevel = "Low",
            IsReversible = false,
            RequiresUserConfirmation = true
        }
    };

    public IEnumerable<RemediationActionDef> GetAllActions() => _actions;

    public RemediationActionDef? GetAction(string id) =>
        _actions.FirstOrDefault(
            a => a.Id.Equals(
                id,
                StringComparison.OrdinalIgnoreCase));

    public IEnumerable<RemediationActionDef> GetActionsByCategory(string category) =>
        _actions.Where(
            a => a.SupportedDiagnosisCategories.Any(
                supportedCategory =>
                    supportedCategory.Equals(
                        category,
                        StringComparison.OrdinalIgnoreCase)));
}
