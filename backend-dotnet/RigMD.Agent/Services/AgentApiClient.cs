using System.Net;
using System.Net.Http.Json;
using RigMD.Agent.Models;

namespace RigMD.Agent.Services;

public class AgentApiClient
{
    private readonly HttpClient _httpClient;

    public AgentApiClient(
        HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task RegisterAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/agent/register");

        AddClientHeader(
            request,
            identity.AgentId);

        request.Content =
            JsonContent.Create(new
            {
                identity.AgentId,
                identity.DeviceName,
                identity.AgentVersion
            });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Agent registration");
    }

    public async Task SendHeartbeatAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/agent/heartbeat");

        AddClientHeader(
            request,
            identity.AgentId);

        request.Content =
            JsonContent.Create(new
            {
                identity.AgentId,
                identity.DeviceName,
                identity.AgentVersion,
                SentAt = DateTimeOffset.UtcNow
            });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Agent heartbeat");
    }

    public async Task SendSnapshotAsync(
        AgentIdentity identity,
        object hardware,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/agent/snapshot");

        AddClientHeader(
            request,
            identity.AgentId);

        request.Content =
            JsonContent.Create(new
            {
                identity.AgentId,
                CapturedAt = DateTimeOffset.UtcNow,
                Hardware = hardware
            });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Agent snapshot");
    }

    public async Task<AgentCommand?> ClaimNextCommandAsync(
        AgentIdentity identity,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/agent/{identity.AgentId}/commands/next");

        AddClientHeader(
            request,
            identity.AgentId);

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(
            response,
            "Claim Agent command");

        return await response.Content
            .ReadFromJsonAsync<AgentCommand>(
                cancellationToken: cancellationToken);
    }

    public async Task CompleteCommandAsync(
        AgentIdentity identity,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/agent/{identity.AgentId}/commands/{commandId}/complete");

        AddClientHeader(
            request,
            identity.AgentId);

        request.Content =
            JsonContent.Create(new { });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Complete Agent command");
    }

    public async Task FailCommandAsync(
        AgentIdentity identity,
        Guid commandId,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/agent/{identity.AgentId}/commands/{commandId}/fail");

        AddClientHeader(
            request,
            identity.AgentId);

        request.Content =
            JsonContent.Create(new
            {
                ErrorMessage = errorMessage
            });

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            "Fail Agent command");
    }

    private static void AddClientHeader(
        HttpRequestMessage request,
        string agentId)
    {
        request.Headers.Add(
            "X-Client-ID",
            agentId);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body =
            await response.Content.ReadAsStringAsync();

        throw new HttpRequestException(
            $"{operation} failed with HTTP " +
            $"{(int)response.StatusCode} " +
            $"{response.StatusCode}. " +
            $"Response: {body}");
    }
}

public class AgentCommand
{
    public Guid Id { get; set; }
    public string AgentId { get; set; } = string.Empty;
    public string CommandType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}