using DarkFactory.Data;

namespace DarkFactory.Mcp;

/// <summary>
/// Runs the server health checks on a schedule (docs/adr/0038). The rules —
/// what a miss is, when a server is unreachable, what healing re-verifies —
/// live in <see cref="ServerHealthService"/>, where they are tested without a
/// clock or a host; this is only the loop.
///
/// It runs in the factory rather than the worker: there is one factory and
/// possibly many workers, and a server checked by every worker is a server
/// pinged N times as often for no better answer.
/// </summary>
public sealed class ServerHealthMonitor(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ServerHealthMonitor> logger) : BackgroundService
{
    /// <summary>How often the monitor asks which servers are due. The cadence per server is the service's.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ServerHealth:Enabled", true))
        {
            logger.LogWarning("Server health checks are disabled (ServerHealth:Enabled=false); statuses will go stale.");
            return;
        }

        using var timer = new PeriodicTimer(Tick);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var health = scope.ServiceProvider.GetRequiredService<ServerHealthService>();
                foreach (var check in await health.CheckDueAsync(DateTimeOffset.UtcNow, stoppingToken))
                {
                    Report(check);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad pass must not end the loop: a monitor that dies on
                // the first database blip stops healing exactly when it is
                // needed.
                logger.LogError(ex, "A server health pass failed; the next tick tries again.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Report(ServerCheck check)
    {
        switch (check.Outcome)
        {
            case ServerCheckOutcome.WentUnreachable:
                logger.LogWarning("Server {Name} ({Id}) is unreachable: {Error}. Next check at {Next:u}.",
                    check.Name, check.ServerId, check.Error, check.NextCheckAt);
                break;
            case ServerCheckOutcome.Healed:
                logger.LogInformation("Server {Name} ({Id}) answered again and was re-verified: {Status}.",
                    check.Name, check.ServerId, check.Status);
                break;
            case ServerCheckOutcome.BrokenHandshake:
                logger.LogWarning("Server {Name} ({Id}) answered with a handshake that no longer validates: {Error}",
                    check.Name, check.ServerId, check.Error);
                break;
            case ServerCheckOutcome.Reverified:
                logger.LogInformation("Server {Name} ({Id}) changed what it says about itself and was re-verified: {Status}.",
                    check.Name, check.ServerId, check.Status);
                break;
            case ServerCheckOutcome.Missed:
            case ServerCheckOutcome.StillUnreachable:
                logger.LogDebug("Server {Name} ({Id}) did not answer: {Error}.", check.Name, check.ServerId, check.Error);
                break;
        }
    }
}
