using System.Text.Json;
using RigMD.Agent.Services;
using RigMD.Agent.Tools;
using RigMD.Application.Contracts.Providers;

namespace RigMD.Agent;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentIdentityService _identityService;
    private readonly AgentApiClient _apiClient;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        AgentIdentityService identityService,
        AgentApiClient apiClient)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _identityService = identityService;
        _apiClient = apiClient;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var identity =
            _identityService.GetOrCreateIdentity();

        _logger.LogInformation(
            "RigMD Windows Agent started.");

        _logger.LogInformation(
            "Agent ID: {AgentId}",
            identity.AgentId);

        _logger.LogInformation(
            "Machine: {MachineName}",
            identity.DeviceName);

        _logger.LogInformation(
            "Agent Version: {Version}",
            identity.AgentVersion);

        _logger.LogInformation(
            "Identity file: {IdentityPath}",
            _identityService.GetIdentityPath());

        await TryRegisterAsync(
            identity,
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope =
                    _scopeFactory.CreateScope();

                var profileService =
                    scope.ServiceProvider
                        .GetRequiredService<
                            IWindowsSystemProfileService>();

                var toolRegistry =
                    scope.ServiceProvider
                        .GetRequiredService<
                            AgentToolRegistry>();

                var profile =
                    profileService
                        .GetLiveSystemProfile();

                var capturedAt =
                    DateTimeOffset.UtcNow;

                var snapshot = new
                {
                    identity.AgentId,
                    identity.DeviceName,
                    identity.AgentVersion,
                    CapturedAt = capturedAt,
                    Hardware = profile
                };

                var json =
                    JsonSerializer.Serialize(
                        snapshot,
                        new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

                _logger.LogInformation(
                    "RigMD system scan completed.");

                Console.WriteLine();
                Console.WriteLine(
                    "======================================");

                Console.WriteLine(
                    "        RigMD Agent System Scan");

                Console.WriteLine(
                    "======================================");

                Console.WriteLine(
                    $"Agent ID: {identity.AgentId}");

                Console.WriteLine(
                    $"Device: {identity.DeviceName}");

                Console.WriteLine(
                    $"Version: {identity.AgentVersion}");

                Console.WriteLine();

                Console.WriteLine(
                    "Available Agent Tools:");

                foreach (var tool in toolRegistry.GetTools())
                {
                    Console.WriteLine(
                        $"- {tool.Id}: {tool.Description}");
                }

                Console.WriteLine();

                if (toolRegistry.TryExecute(
                        "scan_memory",
                        out var memoryResult))
                {
                    var memoryJson =
                        JsonSerializer.Serialize(
                            memoryResult,
                            new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });

                    Console.WriteLine(
                        "Tool Test: scan_memory");

                    Console.WriteLine(
                        memoryJson);

                    Console.WriteLine();
                }

                Console.WriteLine(
                    "Full System Snapshot:");

                Console.WriteLine(json);

                Console.WriteLine(
                    "======================================");

                Console.WriteLine();

                var connected =
                    await EnsureRegisteredAsync(
                        identity,
                        stoppingToken);

                if (connected)
                {
                    try
                    {
                        await _apiClient.SendSnapshotAsync(
                            identity,
                            profile,
                            stoppingToken);

                        _logger.LogInformation(
                            "Agent hardware snapshot sent.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Agent snapshot upload failed. Local scanning will continue.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "RigMD system scan failed.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "RigMD Windows Agent stopped.");
    }

    private async Task<bool> EnsureRegisteredAsync(
        Models.AgentIdentity identity,
        CancellationToken stoppingToken)
    {
        try
        {
            await _apiClient.SendHeartbeatAsync(
                identity,
                stoppingToken);

            _logger.LogInformation(
                "Agent heartbeat sent.");

            return true;
        }
        catch (Exception heartbeatException)
        {
            _logger.LogWarning(
                heartbeatException,
                "Agent heartbeat failed. Attempting re-registration.");

            return await TryRegisterAsync(
                identity,
                stoppingToken);
        }
    }

    private async Task<bool> TryRegisterAsync(
        Models.AgentIdentity identity,
        CancellationToken stoppingToken)
    {
        try
        {
            await _apiClient.RegisterAsync(
                identity,
                stoppingToken);

            _logger.LogInformation(
                "Agent registered with RigMD API.");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Agent could not register with RigMD API. Local scanning will continue.");

            return false;
        }
    }
}