using System.Text.Json;
using RigMD.Agent.Models;
using RigMD.Agent.Services;
using RigMD.Agent.Tools;
using RigMD.Application.Contracts.Providers;
using RigMD.Infrastructure.Remediation.Actions;

namespace RigMD.Agent;

public class Worker : BackgroundService
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromSeconds(5);

    private static readonly TimeSpan HeartbeatInterval =
        TimeSpan.FromSeconds(30);

    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentIdentityService _identityService;
    private readonly AgentApiClient _apiClient;

    private DateTimeOffset _lastHeartbeatAt =
        DateTimeOffset.MinValue;

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

        var registered =
            await TryRegisterAsync(
                identity,
                stoppingToken);

        if (registered)
        {
            _lastHeartbeatAt =
                DateTimeOffset.UtcNow;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connected =
                    await EnsureHeartbeatAsync(
                        identity,
                        stoppingToken);

                if (connected)
                {
                    await ProcessNextCommandAsync(
                        identity,
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Agent command polling failed.");
            }

            try
            {
                await Task.Delay(
                    PollInterval,
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

    private async Task ProcessNextCommandAsync(
        AgentIdentity identity,
        CancellationToken stoppingToken)
    {
        AgentCommand? command;

        try
        {
            command =
                await _apiClient.ClaimNextCommandAsync(
                    identity,
                    stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not check for pending Agent commands.");

            return;
        }

        if (command == null)
        {
            return;
        }

        _logger.LogInformation(
            "Agent command claimed. Command ID: {CommandId}, Type: {CommandType}",
            command.Id,
            command.CommandType);

        try
        {
            switch (command.CommandType)
            {
                case "scan_system_profile":
                    await ExecuteSystemProfileScanAsync(
                        identity,
                        command,
                        stoppingToken);
                    break;
                case "clear_user_temp_files":
                    await ExecuteClearUserTempFilesAsync(
                        identity,
                        command,
                        stoppingToken);
                    break;
                case "flush_dns":
                    await ExecuteFlushDnsAsync(
                        identity,
                        command,
                        stoppingToken);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported Agent command type: {command.CommandType}");
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Agent command failed. Command ID: {CommandId}",
                command.Id);

            try
            {
                await _apiClient.FailCommandAsync(
                    identity,
                    command.Id,
                    GetSafeErrorMessage(ex),
                    stoppingToken);
            }
            catch (Exception failException)
            {
                _logger.LogWarning(
                    failException,
                    "Could not report Agent command failure.");
            }
        }
    }

    private async Task ExecuteClearUserTempFilesAsync(
    AgentIdentity identity,
    AgentCommand command,
    CancellationToken stoppingToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var tempPathResolver =
            scope.ServiceProvider
                .GetRequiredService<InteractiveUserTempPathResolver>();

        var clearTempFilesAction =
            scope.ServiceProvider
                .GetRequiredService<ClearTempFilesAction>();

        var resolvedUser =
            tempPathResolver.Resolve();

        var tempPath =
            resolvedUser.TempPath;

        _logger.LogInformation(
            "Executing allowlisted remediation command. Command ID: {CommandId}, Action: clear_user_temp_files, Account: {AccountName}, TempPath: {TempPath}",
            command.Id,
            resolvedUser.AccountName,
            tempPath);

        var result =
            await clearTempFilesAction.ExecuteAsync(
                tempPath);

        var completionResult =
            new
            {
                ActionId =
                    "clear_user_temp_files",

                ExecutedBy =
                    "RigMD.Agent",

                TempPath =
                    tempPath,

                result.Success,
                result.Summary,
                result.OutputLog,
                result.Proof
            };

        await _apiClient.CompleteCommandAsync(
            identity,
            command.Id,
            completionResult,
            stoppingToken);

        _logger.LogInformation(
            "Allowlisted remediation command completed. Command ID: {CommandId}, Success: {Success}",
            command.Id,
            result.Success);
    }
    private async Task ExecuteFlushDnsAsync(
        AgentIdentity identity,
        AgentCommand command,
        CancellationToken stoppingToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var flushDnsAction =
            scope.ServiceProvider
                .GetRequiredService<FlushDnsAction>();

        _logger.LogInformation(
            "Executing allowlisted remediation command. Command ID: {CommandId}, Action: flush_dns",
            command.Id);

        var result =
            await flushDnsAction
                .ExecuteAsync();

        var completionResult =
            new
            {
                ActionId =
                    "flush_dns",

                ExecutedBy =
                    "RigMD.Agent",

                result.Success,
                result.Summary,
                result.OutputLog,
                result.Proof
            };

        await _apiClient
            .CompleteCommandAsync(
                identity,
                command.Id,
                completionResult,
                stoppingToken);

        _logger.LogInformation(
            "Allowlisted remediation command completed. Command ID: {CommandId}, Action: flush_dns, Success: {Success}",
            command.Id,
            result.Success);
    }
    private async Task ExecuteSystemProfileScanAsync(
        AgentIdentity identity,
        AgentCommand command,
        CancellationToken stoppingToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var toolRegistry =
            scope.ServiceProvider
                .GetRequiredService<AgentToolRegistry>();

        if (!toolRegistry.TryExecute(
                "scan_system_profile",
                out var scanResult))
        {
            throw new InvalidOperationException(
                "scan_system_profile could not be executed.");
        }

        if (scanResult == null)
        {
            throw new InvalidOperationException(
                "scan_system_profile returned no result.");
        }

        var capturedAt =
            DateTimeOffset.UtcNow;

        var json =
            JsonSerializer.Serialize(
                new
                {
                    identity.AgentId,
                    identity.DeviceName,
                    identity.AgentVersion,
                    CapturedAt = capturedAt,
                    Hardware = scanResult
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        _logger.LogInformation(
            "On-demand system scan completed. Command ID: {CommandId}",
            command.Id);

        Console.WriteLine();
        Console.WriteLine(
            "======================================");

        Console.WriteLine(
            "      RigMD On-Demand System Scan");

        Console.WriteLine(
            "======================================");

        Console.WriteLine(
            $"Command ID: {command.Id}");

        Console.WriteLine(
            $"Agent ID: {identity.AgentId}");

        Console.WriteLine(
            $"Captured: {capturedAt:O}");

        Console.WriteLine();

        Console.WriteLine(json);

        Console.WriteLine(
            "======================================");

        Console.WriteLine();

        await _apiClient.SendSnapshotAsync(
            identity,
            command.Id,
            scanResult,
            stoppingToken);

        _logger.LogInformation(
            "Fresh Agent hardware snapshot uploaded. Command ID: {CommandId}",
            command.Id);

        await _apiClient.CompleteCommandAsync(
            identity,
            command.Id,
            null,
            stoppingToken);

        _logger.LogInformation(
            "Agent command completed. Command ID: {CommandId}",
            command.Id);
    }

    private async Task<bool> EnsureHeartbeatAsync(
        AgentIdentity identity,
        CancellationToken stoppingToken)
    {
        if (DateTimeOffset.UtcNow -
            _lastHeartbeatAt <
            HeartbeatInterval)
        {
            return true;
        }

        try
        {
            await _apiClient.SendHeartbeatAsync(
                identity,
                stoppingToken);

            _lastHeartbeatAt =
                DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Agent heartbeat sent.");

            return true;
        }
        catch (Exception heartbeatException)
        {
            _logger.LogWarning(
                heartbeatException,
                "Agent heartbeat failed. Attempting re-registration.");

            var registered =
                await TryRegisterAsync(
                    identity,
                    stoppingToken);

            if (registered)
            {
                _lastHeartbeatAt =
                    DateTimeOffset.UtcNow;
            }

            return registered;
        }
    }

    private async Task<bool> TryRegisterAsync(
        AgentIdentity identity,
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
                "Agent could not register with RigMD API. Local Agent will continue.");

            return false;
        }
    }

    private static string GetSafeErrorMessage(
        Exception exception)
    {
        var message =
            string.IsNullOrWhiteSpace(exception.Message)
                ? "Agent command failed."
                : exception.Message.Trim();

        return message.Length > 1000
            ? message[..1000]
            : message;
    }
}