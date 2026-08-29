namespace RigMD.Agent.Tools;

public interface IAgentTool
{
    string Id { get; }

    string Description { get; }

    object Execute();
}