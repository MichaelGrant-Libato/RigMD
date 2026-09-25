using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace RigMD.Api.Hubs;

public class RemediationHub : Hub
{
    public async Task JoinSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        }
    }

    public async Task LeaveSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        }
    }
}
