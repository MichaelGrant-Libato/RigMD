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
                "Elevated Storage Utilization"
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
                "OS performance degradation"
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
