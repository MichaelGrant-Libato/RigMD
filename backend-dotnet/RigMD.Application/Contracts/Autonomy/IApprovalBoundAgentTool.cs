namespace RigMD.Application.Contracts.Autonomy;

// Execution context comes only from a consumed server-issued approval, never from request arguments.
public interface IApprovalBoundAgentTool
{
    bool TryBindApprovalContext(object context);
}
