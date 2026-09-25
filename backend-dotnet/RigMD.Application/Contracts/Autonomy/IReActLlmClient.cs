using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Models;

namespace RigMD.Application.Contracts.Autonomy;

/// <summary>
/// Contract for the ReAct reasoning model client (Google Gemini free-tier function calling with local tool-calling fallback).
/// </summary>
public interface IReActLlmClient
{
    Task<ReActModelTurnDecision> DecideNextTurnAsync(
        ReActConversationContext context,
        IReadOnlyList<AgentToolFunctionDeclaration> availableTools,
        CancellationToken cancellationToken = default);
}
