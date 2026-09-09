using Microsoft.AspNetCore.SignalR;

namespace RigMD.Api.Hubs;

public class RemediationHub : Hub
{
    // The Hub is intentionally empty for now.
    // The backend uses IHubContext<RemediationHub> to push messages
    // to the "ReceiveProgress" client method.
}
