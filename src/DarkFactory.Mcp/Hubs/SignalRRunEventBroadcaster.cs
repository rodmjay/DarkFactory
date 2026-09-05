using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.AspNetCore.SignalR;

namespace DarkFactory.Mcp.Hubs;

public sealed class SignalRRunEventBroadcaster(IHubContext<RunHub> hubContext) : IRunEventBroadcaster
{
    public Task BroadcastAsync(Event runEvent, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RunHub.GroupName(runEvent.RunId)).SendAsync(
            "runEvent",
            new { runEvent.Id, runEvent.RunId, runEvent.Type, runEvent.DataJson, runEvent.CreatedAt },
            cancellationToken);
}
