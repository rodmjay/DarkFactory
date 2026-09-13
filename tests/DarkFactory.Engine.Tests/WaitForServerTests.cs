using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DarkFactory.Engine.Tests;

// docs/adr/0038: a run whose workspace server is unreachable waits for it.
// The property that matters is the one retry-and-fail would violate: a
// server restart costs the run nothing — no attempt, no failure — and the
// run carries on once the monitor has re-verified the server.
[Collection("Engine")]
public class WaitForServerTests(EngineTestFixture fixture)
{
    /// <summary>
    /// Succeeds at every stage, counting only this test's run. The engine
    /// database is shared, so the state machine may claim another test's
    /// leftover run first; a handler for every stage means that cannot throw,
    /// and counting by run id means it cannot inflate this test's count.
    /// </summary>
    private sealed class CountingHandler(StageId stage, Func<string?> runId) : IStageHandler
    {
        public int Calls;

        public StageId Stage => stage;

        public Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
        {
            if (context.Run.Id == runId()) Interlocked.Increment(ref Calls);
            return Task.FromResult<StageOutcome>(new StageOutcome.Success());
        }
    }

    private static readonly EngineOptions Options_ = new()
    {
        WorkerId = "wait-test", LeaseDurationSeconds = 30, RetryBackoffBaseSeconds = 0.01, MaxAttempts = 2,
    };

    private async Task ProcessOneAsync(IReadOnlyList<IStageHandler> handlers)
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        await new RunStateMachine(
            db, new RunLeaseStore(db), new PostgresArtifactStore(db), handlers,
            Options.Create(Options_), NullLogger<RunStateMachine>.Instance).TryProcessOneAsync();
    }

    private async Task<Run> LoadAsync(string runId)
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        return await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
    }

    private async Task UpdateAsync(Func<DarkFactoryDbContext, Task> change)
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        await change(db);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_run_whose_workspace_is_unreachable_waits_without_spending_an_attempt()
    {
        Run run;
        var nextCheck = DateTimeOffset.UtcNow.AddMinutes(2);
        await using (var db = TestDb.Create(fixture.ConnectionString))
        {
            run = await TestSeed.SeedRunAsync(db);
            var project = await db.Projects.SingleAsync(p => p.Id == run.ProjectId);
            db.Servers.Add(new Server
            {
                Id = Guid.NewGuid().ToString("n"),
                OrgId = project.OrgId,
                Url = project.WorkspaceMcpUrl,
                Name = "drones-workspace",
                Tier = ServerTier.BuiltIn,
                Domain = "workspace",
                ConventionVersion = "0.2.0",
                ManifestJson = "{}",
                Status = ServerStatus.Unreachable,
                RegisteredAt = DateTimeOffset.UtcNow.AddDays(-1),
                UnreachableSince = DateTimeOffset.UtcNow.AddMinutes(-1),
                LastError = "connection refused",
                NextCheckAt = nextCheck,
            });
            await db.SaveChangesAsync();
        }

        var handlers = Enum.GetValues<StageId>().Select(s => new CountingHandler(s, () => run.Id)).ToList();
        var handler = handlers.Single(h => h.Stage == StageId.Intake);

        try
        {
            await WaitThenResumeAsync(run, nextCheck, handlers, handler);
        }
        finally
        {
            // Leave the shared database as it was found: a run left Pending
            // is claimed first by the next test's state machine, which has no
            // handler for wherever this one stopped.
            await UpdateAsync(async db =>
            {
                var mine = await db.Runs.SingleAsync(r => r.Id == run.Id);
                mine.Status = RunStatus.Completed;
                mine.LeasedBy = null;
                mine.LeaseExpiresAt = null;
            });
        }
    }

    private async Task WaitThenResumeAsync(
        Run run, DateTimeOffset nextCheck, IReadOnlyList<IStageHandler> handlers, CountingHandler handler)
    {
        // The shared database may hold other runnable runs; process until
        // this one has been looked at.
        for (var i = 0; i < 20 && (await LoadAsync(run.Id)).LeaseExpiresAt is null; i++)
        {
            await ProcessOneAsync(handlers);
        }

        var waiting = await LoadAsync(run.Id);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(RunStatus.Running, waiting.Status);
        Assert.Equal(StageId.Intake, waiting.CurrentStage);
        Assert.InRange(Math.Abs((waiting.LeaseExpiresAt!.Value - nextCheck).TotalMilliseconds), 0, 5);

        // Looked at again while still down: still waiting, and said once.
        await UpdateAsync(async db =>
            (await db.Runs.SingleAsync(r => r.Id == run.Id)).LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1));
        for (var i = 0; i < 20 && (await LoadAsync(run.Id)).LeaseExpiresAt < DateTimeOffset.UtcNow; i++)
        {
            await ProcessOneAsync(handlers);
        }
        Assert.Equal(0, handler.Calls);

        await using (var db = TestDb.Create(fixture.ConnectionString))
        {
            Assert.Equal(1, await db.Events.CountAsync(e => e.RunId == run.Id && e.Type == "run.waiting_for_server"));
            Assert.Equal(0, await db.StageCheckpoints.CountAsync(c => c.RunId == run.Id));
        }

        // The monitor heals the server; the run carries on at attempt 1.
        await UpdateAsync(async db =>
        {
            var project = await db.Projects.SingleAsync(p => p.Id == run.ProjectId);
            (await db.Servers.SingleAsync(s => s.Url == project.WorkspaceMcpUrl)).Status = ServerStatus.Conformant;
            (await db.Runs.SingleAsync(r => r.Id == run.Id)).LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        });
        for (var i = 0; i < 20 && (await LoadAsync(run.Id)).CurrentStage == StageId.Intake; i++)
        {
            await ProcessOneAsync(handlers);
        }

        Assert.Equal(1, handler.Calls);
        await using (var db = TestDb.Create(fixture.ConnectionString))
        {
            var checkpoint = await db.StageCheckpoints.SingleAsync(c => c.RunId == run.Id);
            Assert.Equal(RunStatus.Completed, checkpoint.Status);
            Assert.Equal(1, checkpoint.Attempt);
        }
    }
}
