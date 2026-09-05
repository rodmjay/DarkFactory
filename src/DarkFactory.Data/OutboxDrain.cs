using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Where an outbox row actually goes once it's ready to leave Postgres
/// (docs/adr/0008 and docs/adr/0006's four channels). v1 implementation
/// broadcasts to the SignalR hub (DarkFactory.Mcp); "and (later) Service
/// Bus" is why this is an interface rather than a hard SignalR dependency
/// here in Data.
/// </summary>
public interface IRunEventBroadcaster
{
    Task BroadcastAsync(Core.Event runEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outbox drain, factored out from any hosting/scheduling concern so it
/// can be exercised directly in tests: call <see cref="DrainOnceAsync"/>
/// against a real Postgres and a fake <see cref="IRunEventBroadcaster"/>
/// and assert on what got marked published. The actual polling loop
/// (DarkFactory.Mcp's OutboxPublisherHostedService) is a thin wrapper that
/// calls this on a timer, next to the SignalR hub it broadcasts through.
/// </summary>
public static class OutboxDrain
{
    public static async Task<int> DrainOnceAsync(
        DarkFactoryDbContext dbContext,
        IRunEventBroadcaster broadcaster,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var unpublished = await dbContext.Events
            .Where(e => e.PublishedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        foreach (var runEvent in unpublished)
        {
            await broadcaster.BroadcastAsync(runEvent, cancellationToken);
            runEvent.PublishedAt = DateTimeOffset.UtcNow;
        }

        if (unpublished.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return unpublished.Count;
    }
}
