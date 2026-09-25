using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RigMD.Application.Contracts.Autonomy;
using RigMD.Application.Contracts.Providers;
using RigMD.Application.Models;

namespace RigMD.Infrastructure.Remediation.Tools.Diagnostic;

public class InspectNetworkConnectivityTool : IRigMdAgentTool
{
    private readonly INetworkProvider _networkProvider;

    public InspectNetworkConnectivityTool(INetworkProvider networkProvider)
    {
        _networkProvider = networkProvider;
    }

    public string Name => "inspect_network_connectivity";

    public string DisplayName => "Inspect Network Adapter, DNS & Ping";

    public string Description =>
        "Inspects active network adapter status, IPv4/gateway/DNS configuration, Wi-Fi signal strength, and runs a live timed DNS resolution and ICMP ping probe against a target hostname.";

    public ToolSafetyTier SafetyTier => ToolSafetyTier.Tier0_ReadOnly;

    public AgentToolFunctionDeclaration GetFunctionDeclaration()
    {
        return new AgentToolFunctionDeclaration
        {
            Name = Name,
            Description = Description,
            Parameters = new Dictionary<string, AgentToolParameterProperty>
            {
                ["targetHost"] = new()
                {
                    Type = "string",
                    Description = "Optional hostname to test live DNS resolution and ping against (default: 'one.one.one.one')."
                }
            },
            Required = new List<string>()
        };
    }

    public Task<ToolDryRunPreview> PreviewImpactAsync(
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolDryRunPreview
        {
            ToolName = Name,
            DisplayName = DisplayName,
            SafetyTier = SafetyTier,
            CanExecute = true,
            RequiresAdmin = false,
            IsRunningAsAdmin = ToolArgumentHelper.IsCurrentProcessElevated(),
            RequiresUserConfirmation = false,
            WhatWillHappen = "Checks active network interfaces and performs a read-only DNS lookup and ping probe."
        });
    }

    public async Task<AgentToolExecutionResult> ExecuteAsync(
        JsonElement arguments,
        Action<string>? progressReporter = null,
        CancellationToken cancellationToken = default)
    {
        var targetHost = ToolArgumentHelper.GetString(arguments, "targetHost", "one.one.one.one");
        if (string.IsNullOrWhiteSpace(targetHost))
        {
            targetHost = "one.one.one.one";
        }

        progressReporter?.Invoke($"Inspecting network adapter and probing DNS/ping to '{targetHost}'...");

        var net = _networkProvider.GetNetworkStats();

        bool liveDnsOk = false;
        long dnsLookupMs = 0;
        var resolvedAddresses = new List<string>();
        string? dnsError = null;

        var sw = Stopwatch.StartNew();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(targetHost, cancellationToken);
            sw.Stop();
            dnsLookupMs = sw.ElapsedMilliseconds;
            resolvedAddresses = addresses.Select(a => a.ToString()).Take(4).ToList();
            liveDnsOk = resolvedAddresses.Count > 0;
        }
        catch (Exception ex)
        {
            sw.Stop();
            dnsLookupMs = sw.ElapsedMilliseconds;
            dnsError = ex.Message;
        }

        long? livePingMs = net.PingLatencyMs;
        string? pingStatus = null;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(targetHost, TimeSpan.FromMilliseconds(2000), cancellationToken: cancellationToken);
            pingStatus = reply.Status.ToString();
            if (reply.Status == IPStatus.Success)
            {
                livePingMs = reply.RoundtripTime;
            }
        }
        catch (Exception ex)
        {
            pingStatus = ex.Message;
        }

        var payload = new
        {
            hasActiveAdapter = net.HasActiveAdapter,
            adapterName = net.AdapterName,
            ipAddress = net.IpAddress,
            macAddress = net.MacAddress,
            hasIpv4Address = net.HasIpv4Address,
            hasDefaultGateway = net.HasDefaultGateway,
            hasDnsServers = net.HasDnsServers,
            isWifi = net.IsWifi,
            wifiSignalStrengthPercent = net.WifiSignalStrength,
            packetLossPercent = net.PacketLossPercent,
            liveProbe = new
            {
                targetHost,
                dnsResolutionSucceeded = liveDnsOk,
                dnsLookupMs,
                resolvedAddresses,
                dnsError,
                pingStatus,
                pingLatencyMs = livePingMs
            }
        };

        var summary =
            $"Adapter '{net.AdapterName}' (Active: {net.HasActiveAdapter}, Gateway: {net.HasDefaultGateway}, DNS Servers: {net.HasDnsServers}). Probe '{targetHost}': DNS={(liveDnsOk ? $"OK ({dnsLookupMs} ms)" : $"FAILED ({dnsError})")}, Ping={(livePingMs.HasValue ? $"{livePingMs} ms" : pingStatus ?? "N/A")}.";

        return new AgentToolExecutionResult
        {
            ToolName = Name,
            Success = true,
            Summary = summary,
            DataJson = JsonSerializer.Serialize(payload, ToolArgumentHelper.JsonOptions),
            OutputLog = summary
        };
    }
}
