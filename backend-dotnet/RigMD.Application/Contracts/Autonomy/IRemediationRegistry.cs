using System.Collections.Generic;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

public interface IRemediationRegistry
{
    RemediationActionDef? GetAction(string actionId);
    IEnumerable<RemediationActionDef> GetAllActions();
    IEnumerable<RemediationActionDef> GetActionsByCategory(string category);
}
