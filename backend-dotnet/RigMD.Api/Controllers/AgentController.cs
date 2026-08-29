using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Models;
using RigMD.Application.Contracts.Persistence;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private readonly IAgentRepository _agentRepository;

    public AgentController(
        IAgentRepository agentRepository)
    {
        _agentRepository = agentRepository;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] AgentRegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        var clientId =
            HttpContext.Items["ClientId"]?
                .ToString();

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new
            {
                message = "Client ID is required."
            });
        }

        var agent =
            await _agentRepository.RegisterAsync(
                request.AgentId,
                clientId,
                request.DeviceName,
                request.AgentVersion,
                cancellationToken);

        return Ok(ToStatus(agent));
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromBody] AgentHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        var agent =
            await _agentRepository.HeartbeatAsync(
                request.AgentId,
                request.DeviceName,
                request.AgentVersion,
                cancellationToken);

        if (agent == null)
        {
            return NotFound(new
            {
                message =
                    "Agent is not registered."
            });
        }

        return Ok(ToStatus(agent));
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> Snapshot(
        [FromBody] AgentSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        var agent =
            await _agentRepository.GetAgentAsync(
                request.AgentId,
                cancellationToken);

        if (agent == null)
        {
            return NotFound(new
            {
                message =
                    "Agent is not registered."
            });
        }

        var hardwareJson =
            JsonSerializer.Serialize(
                request.Hardware);

        await _agentRepository.SaveSnapshotAsync(
            request.AgentId,
            request.CapturedAt,
            hardwareJson,
            cancellationToken);

        return Ok(new
        {
            message = "Snapshot received.",
            request.AgentId,
            request.CapturedAt
        });
    }

    [HttpGet("{agentId}")]
    public async Task<IActionResult> GetAgent(
        string agentId,
        CancellationToken cancellationToken)
    {
        var agent =
            await _agentRepository.GetAgentAsync(
                agentId,
                cancellationToken);

        if (agent == null)
        {
            return NotFound(new
            {
                message = "Agent was not found."
            });
        }

        return Ok(ToStatus(agent));
    }

    [HttpGet("{agentId}/snapshot")]
    public async Task<IActionResult> GetSnapshot(
        string agentId,
        CancellationToken cancellationToken)
    {
        var snapshot =
            await _agentRepository
                .GetLatestSnapshotAsync(
                    agentId,
                    cancellationToken);

        if (snapshot == null)
        {
            return NotFound(new
            {
                message =
                    "No snapshot found for this agent."
            });
        }

        using var hardware =
            JsonDocument.Parse(
                snapshot.HardwareJson);

        return Ok(new
        {
            snapshot.AgentId,
            snapshot.CapturedAt,
            Hardware =
                hardware.RootElement.Clone()
        });
    }

    private static object ToStatus(
        AgentDeviceRecord agent)
    {
        var isOnline =
            DateTimeOffset.UtcNow -
            agent.LastSeen <
            TimeSpan.FromMinutes(2);

        return new
        {
            agent.AgentId,
            agent.ClientId,
            agent.DeviceName,
            agent.AgentVersion,
            agent.RegisteredAt,
            agent.LastSeen,
            IsOnline = isOnline
        };
    }
}