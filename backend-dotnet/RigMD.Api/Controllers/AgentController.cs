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
                message = "Agent is not registered."
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
                message = "Agent is not registered."
            });
        }

        var hardwareJson =
            JsonSerializer.Serialize(
                request.Hardware);

        await _agentRepository.SaveSnapshotAsync(
            request.AgentId,
            request.CommandId,
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

    [HttpPost("{agentId}/scan-request")]
    public async Task<IActionResult> CreateScanRequest(
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

        var command =
            await _agentRepository.CreateCommandAsync(
                agentId,
                "scan_system_profile",
                cancellationToken);

        return Ok(ToCommand(command));
    }

    [HttpGet("{agentId}/commands/next")]
    public async Task<IActionResult> ClaimNextCommand(
        string agentId,
        CancellationToken cancellationToken)
    {
        var command =
            await _agentRepository.ClaimNextCommandAsync(
                agentId,
                cancellationToken);

        if (command == null)
        {
            return NoContent();
        }

        return Ok(ToCommand(command));
    }

    [HttpGet("{agentId}/commands/{commandId:guid}")]
    public async Task<IActionResult> GetCommand(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var command =
            await _agentRepository.GetCommandAsync(
                agentId,
                commandId,
                cancellationToken);

        if (command == null)
        {
            return NotFound(new
            {
                message = "Agent command was not found."
            });
        }

        return Ok(ToCommand(command));
    }

    [HttpPost("{agentId}/commands/{commandId:guid}/complete")]
    public async Task<IActionResult> CompleteCommand(
        string agentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        var command =
            await _agentRepository.CompleteCommandAsync(
                agentId,
                commandId,
                cancellationToken);

        if (command == null)
        {
            return NotFound(new
            {
                message =
                    "Running Agent command was not found."
            });
        }

        return Ok(ToCommand(command));
    }

    [HttpPost("{agentId}/commands/{commandId:guid}/fail")]
    public async Task<IActionResult> FailCommand(
        string agentId,
        Guid commandId,
        [FromBody] AgentCommandFailureRequest request,
        CancellationToken cancellationToken)
    {
        var errorMessage =
            string.IsNullOrWhiteSpace(request.ErrorMessage)
                ? "Agent command failed."
                : request.ErrorMessage.Trim();

        if (errorMessage.Length > 1000)
        {
            errorMessage =
                errorMessage[..1000];
        }

        var command =
            await _agentRepository.FailCommandAsync(
                agentId,
                commandId,
                errorMessage,
                cancellationToken);

        if (command == null)
        {
            return NotFound(new
            {
                message =
                    "Running Agent command was not found."
            });
        }

        return Ok(ToCommand(command));
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

    private static object ToCommand(
        AgentCommandRecord command)
    {
        return new
        {
            command.Id,
            command.AgentId,
            command.CommandType,
            command.Status,
            command.RequestedAt,
            command.ClaimedAt,
            command.CompletedAt,
            command.ErrorMessage
        };
    }
}