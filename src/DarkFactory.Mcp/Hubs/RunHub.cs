using Microsoft.AspNetCore.SignalR;

namespace DarkFactory.Mcp.Hubs;

/// <summary>
/// Real-time push to the dashboard. Step 1 wires the hub with no server-sent
/// events yet; the engine (step 2) and dashboard (step 4) will broadcast
/// run/stage/gate/event updates through this hub as they land.
/// </summary>
public sealed class RunHub : Hub
{
    public async Task JoinRunGroup(string runId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(runId));

    public async Task LeaveRunGroup(string runId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(runId));

    public static string GroupName(string runId) => $"run:{runId}";
}
