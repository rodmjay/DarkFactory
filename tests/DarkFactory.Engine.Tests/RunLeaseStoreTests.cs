using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Tests;

// Covers docs/adr/0008's "lease, not just lock": FOR UPDATE SKIP LOCKED
// alone would let a run be re-claimed the instant a crashed worker's
// session disconnects. LeaseExpiresAt is what actually gates re-claiming,
// and it's tested deterministically here by setting it directly rather
// than waiting in real time for a lease to expire.
[Collection("Engine")]
public class RunLeaseStoreTests(EngineTestFixture fixture)
{
    [Fact]
    public async Task ClaimNextRunnableAsync_claims_a_pending_run()
    {
        string runId;
        await using (var seedDb = TestDb.Create(fixture.ConnectionString))
        {
            runId = (await TestSeed.SeedRunAsync(seedDb)).Id;
        }

        // A fresh DbContext for the claim itself: reusing the seeding
        // context would let EF's identity map hand back the pre-claim
        // tracked instance instead of what the claim query just wrote —
        // see the comment on RunStateMachineHappyPathTests.ProcessOneAsync.
        await using var db = TestDb.Create(fixture.ConnectionString);
        var claimed = await new RunLeaseStore(db).ClaimNextRunnableAsync("worker-a", TimeSpan.FromSeconds(30));

        Assert.NotNull(claimed);
        Assert.Equal(runId, claimed!.Id);
        Assert.Equal("worker-a", claimed.LeasedBy);
        Assert.Equal(RunStatus.Running, claimed.Status);
        Assert.NotNull(claimed.LeaseExpiresAt);
    }

    [Fact]
    public async Task ClaimNextRunnableAsync_does_not_reclaim_a_run_with_an_active_lease()
    {
        await using var db1 = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db1);
        var first = await new RunLeaseStore(db1).ClaimNextRunnableAsync("worker-a", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        await using var db2 = TestDb.Create(fixture.ConnectionString);
        var second = await new RunLeaseStore(db2)
            .ClaimNextRunnableAsync("worker-b", TimeSpan.FromSeconds(30), CancellationToken.None);

        // The only run that exists is already leased and not expired —
        // nothing else is claimable for worker-b to grab.
        Assert.True(second is null || second.Id != run.Id);
    }

    [Fact]
    public async Task ClaimNextRunnableAsync_reclaims_once_the_lease_has_expired()
    {
        await using var db1 = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db1);
        var first = await new RunLeaseStore(db1).ClaimNextRunnableAsync("worker-a", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        // Simulate worker-a crashing and its lease running out — set the
        // expiry into the past directly rather than sleeping past it.
        await db1.Runs.Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseExpiresAt, _ => DateTimeOffset.UtcNow.AddSeconds(-1)));

        await using var db2 = TestDb.Create(fixture.ConnectionString);
        var reclaimed = await new RunLeaseStore(db2).ClaimNextRunnableAsync("worker-b", TimeSpan.FromSeconds(30));

        Assert.NotNull(reclaimed);
        Assert.Equal(run.Id, reclaimed!.Id);
        Assert.Equal("worker-b", reclaimed.LeasedBy);
    }

    [Fact]
    public async Task RenewLeaseAsync_fails_once_a_different_worker_holds_the_lease()
    {
        await using var db1 = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db1);
        await new RunLeaseStore(db1).ClaimNextRunnableAsync("worker-a", TimeSpan.FromSeconds(30));

        await using var db2 = TestDb.Create(fixture.ConnectionString);
        var renewedByStranger = await new RunLeaseStore(db2)
            .RenewLeaseAsync(run.Id, "worker-b", TimeSpan.FromSeconds(30));

        Assert.False(renewedByStranger);

        await using var db3 = TestDb.Create(fixture.ConnectionString);
        var renewedByOwner = await new RunLeaseStore(db3)
            .RenewLeaseAsync(run.Id, "worker-a", TimeSpan.FromSeconds(30));

        Assert.True(renewedByOwner);
    }
}
