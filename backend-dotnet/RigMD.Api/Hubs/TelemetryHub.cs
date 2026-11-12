using Microsoft.AspNetCore.SignalR;
using RigMD.Api.Services;

namespace RigMD.Api.Hubs;

public sealed class TelemetryHub(TelemetrySubscriptions subscriptions) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        subscriptions.Add(Context.ConnectionId);
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
