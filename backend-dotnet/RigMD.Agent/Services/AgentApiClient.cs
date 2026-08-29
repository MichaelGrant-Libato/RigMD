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

        request.Headers.Add(
            "X-Client-ID",
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

        request.Headers.Add(
            "X-Client-ID",
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

        request.Headers.Add(
            "X-Client-ID",
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