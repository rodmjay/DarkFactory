using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Tests;

// Covers docs/adr/0008's transactional outbox: a stage's checkpoint and its
// event commit together (RunStateMachineHappyPathTests exercises that
// side); this covers the drain side — that OutboxDrain finds unpublished
// events, hands each to the broadcaster exactly once, and marks it
// published so it isn't redelivered on the next poll.
//
// DrainOnceAsync intentionally drains the whole table, not one run — that's
// the correct outbox behavior. Since every test in this collection shares
// one Postgres instance sequentially, other tests' own (never-drained)
// events are routinely still sitting there unpublished when these run.
// Assertions below are scoped to this test's own event id(s) rather than
// the raw drained count, so they hold regardless of what else has piled up.
[Collection("Engine")]
public class OutboxDrainTests(EngineTestFixture fixture)
{
    private sealed class RecordingBroadcaster : IRunEventBroadcaster
    {
        public List<Event> Broadcast { get; } = [];

        public Task BroadcastAsync(Event runEvent, CancellationToken cancellationToken = default)
        {
            Broadcast.Add(runEvent);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DrainOnceAsync_broadcasts_unpublished_events_and_marks_them_published()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db);

        var eventIds = new[] { Guid.NewGuid().ToString("n"), Guid.NewGuid().ToString("n") };
        db.Events.AddRange(
            new Event { Id = eventIds[0], RunId = run.Id, Type = "stage.completed", CreatedAt = DateTimeOffset.UtcNow },
            new Event { Id = eventIds[1], RunId = run.Id, Type = "gate.waiting", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var broadcaster = new RecordingBroadcaster();
        await OutboxDrain.DrainOnceAsync(db, broadcaster);

        Assert.All(eventIds, id => Assert.Contains(broadcaster.Broadcast, e => e.Id == id));

        var ownStillUnpublished = await db.Events.CountAsync(e => e.RunId == run.Id && e.PublishedAt == null);
        Assert.Equal(0, ownStillUnpublished);
    }

    [Fact]
    public async Task DrainOnceAsync_does_not_redeliver_already_published_events()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db);
        var eventId = Guid.NewGuid().ToString("n");
        db.Events.Add(new Event { Id = eventId, RunId = run.Id, Type = "stage.completed", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var broadcaster = new RecordingBroadcaster();
        await OutboxDrain.DrainOnceAsync(db, broadcaster);
        await OutboxDrain.DrainOnceAsync(db, broadcaster);

        // Broadcast exactly once across both drains — not rebroadcast by
        // the second one just because other tests' events shared the table.
        Assert.Equal(1, broadcaster.Broadcast.Count(e => e.Id == eventId));
    }
}
