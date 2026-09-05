using DarkFactory.Data;

namespace DarkFactory.Mcp.Hubs;

/// <summary>
/// Polls for unpublished events (docs/adr/0008's outbox) and broadcasts
/// them over SignalR. Runs inside `factory`, not `worker`, since that's
/// where the hub lives; nothing about a stage handler producing an event
/// depends on this loop being up — see OutboxDrain.cs.
/// </summary>
public sealed class OutboxPublisherHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DarkFactoryDbContext>();
                var broadcaster = scope.ServiceProvider.GetRequiredService<IRunEventBroadcaster>();
                await OutboxDrain.DrainOnceAsync(dbContext, broadcaster, cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox drain failed; retrying after the usual poll interval.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
