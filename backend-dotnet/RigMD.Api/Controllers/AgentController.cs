using Microsoft.AspNetCore.Mvc;
using RigMD.Api.Models;

namespace RigMD.Api.Controllers;

[ApiController]
[Route("api/agent")]
public class AgentController : ControllerBase
{
    private static readonly Dictionary<string, AgentStatus> Agents = new();
    private static readonly Dictionary<string, AgentSnapshotRequest> Snapshots = new();

    [HttpPost("register")]
    public IActionResult Register(
        [FromBody] AgentRegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        var status = new AgentStatus
        {
            AgentId = request.AgentId,
            DeviceName = request.DeviceName,
            AgentVersion = request.AgentVersion,
            LastSeen = DateTimeOffset.UtcNow,
            IsOnline = true
        };

        Agents[request.AgentId] = status;

        return Ok(status);
    }

    [HttpPost("heartbeat")]
    public IActionResult Heartbeat(
        [FromBody] AgentHeartbeatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        if (!Agents.TryGetValue(
                request.AgentId,
                out var status))
        {
            return NotFound(new
            {
                message = "Agent is not registered."
            });
        }

        status.DeviceName = request.DeviceName;
        status.AgentVersion = request.AgentVersion;
        status.LastSeen = DateTimeOffset.UtcNow;
        status.IsOnline = true;

        return Ok(status);
    }

    [HttpPost("snapshot")]
    public IActionResult Snapshot(
        [FromBody] AgentSnapshotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            return BadRequest(new
            {
                message = "Agent ID is required."
            });
        }

        if (!Agents.ContainsKey(request.AgentId))
        {
            return NotFound(new
            {
                message = "Agent is not registered."
            });
        }

        Snapshots[request.AgentId] = request;

        return Ok(new
        {
            message = "Snapshot received.",
            request.AgentId,
            request.CapturedAt
        });
    }

    [HttpGet("{agentId}")]
    public IActionResult GetAgent(
        string agentId)
    {
        if (!Agents.TryGetValue(
                agentId,
                out var status))
        {
            return NotFound(new
            {
                message = "Agent was not found."
            });
        }

        var online =
            DateTimeOffset.UtcNow -
            status.LastSeen <
            TimeSpan.FromMinutes(2);

        status.IsOnline = online;

        return Ok(status);
    }

    [HttpGet("{agentId}/snapshot")]
    public IActionResult GetSnapshot(
        string agentId)
    {
        if (!Snapshots.TryGetValue(
                agentId,
                out var snapshot))
        {
            return NotFound(new
            {
                message = "No snapshot found for this agent."
            });
        }

        return Ok(snapshot);
    }

    public class AgentStatus
    {
        public string AgentId { get; set; } =
            string.Empty;

        public string DeviceName { get; set; } =
            string.Empty;

        public string AgentVersion { get; set; } =
            string.Empty;

        public DateTimeOffset LastSeen { get; set; }

        public bool IsOnline { get; set; }
    }
}