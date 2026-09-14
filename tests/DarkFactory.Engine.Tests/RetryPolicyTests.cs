using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DarkFactory.Engine.Tests;

// Covers docs/adr/0007's retry policy: the engine — not the handler — owns
// backoff and the retryable/permanent/needs_human split. A tiny
// RetryBackoffBaseSeconds keeps this fast while still exercising the real
// lease-as-backoff-clock mechanism (RunStateMachine reuses LeaseExpiresAt
// for backoff — see its ApplyFailureAsync), rather than mocking time away.
[Collection("Engine")]
public class RetryPolicyTests(EngineTestFixture fixture)
{
    /// <summary>Fails until the Nth attempt at this stage, decided by querying checkpoint history —
    /// a real handler is stateless across calls/processes, so the test double is too.</summary>
    private sealed class FlakyHandler(StageId stage, int succeedsOnAttempt) : IStageHandler
    {
        public StageId Stage => stage;

        public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
        {
            var priorAttempts = await context.DbContext.StageCheckpoints
                .CountAsync(c => c.RunId == context.Run.Id && c.Stage == stage, cancellationToken);
            var thisAttempt = priorAttempts + 1;

            return thisAttempt < succeedsOnAttempt
                ? new StageOutcome.Failed(FailureClass.Retryable, $"synthetic failure #{thisAttempt}")
                : new StageOutcome.Success();
        }
    }

    private sealed class AlwaysFailsHandler(StageId stage) : IStageHandler
    {
        public StageId Stage => stage;

        public Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken) =>
            Task.FromResult<StageOutcome>(new StageOutcome.Failed(FailureClass.Retryable, "always fails"));
    }

    private static async Task<StageId?> ProcessOneAsync(string connectionString, IStageHandler handler, EngineOptions options)
    {
        await using var db = TestDb.Create(connectionString);
        var stateMachine = new RunStateMachine(
            db, new RunLeaseStore(db), new PostgresArtifactStore(db), [handler],
            Options.Create(options), NullLogger<RunStateMachine>.Instance);
        return await stateMachine.TryProcessOneAsync();
    }

    /// <summary>Repeatedly tries to process the run's current stage until the handler succeeds
    /// (i.e. the stage advances past `stage`), bounded by a timeout — not a fixed sleep.</summary>
    private static async Task DriveUntilPastStageAsync(string connectionString, IStageHandler handler, EngineOptions options, string runId, StageId stage, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await ProcessOneAsync(connectionString, handler, options);

            await using var db = TestDb.Create(connectionString);
            var run = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            if (run.CurrentStage != stage || run.Status == RunStatus.Failed)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException($"Run {runId} never moved past {stage} within {timeout}.");
    }

    /// <summary>
    /// The engine database is shared across tests and the state machine
    /// claims the oldest runnable run, whoever's it is. A run these tests
    /// left Pending at Spec was claimed first by the next test, whose handler
    /// only knows Intake — so whether these passed depended on the order they
    /// ran in. Every run is retired when its test ends, after its assertions.
    /// </summary>
    private static async Task RetireAsync(string connectionString, string runId)
    {
        await using var db = TestDb.Create(connectionString);
        var run = await db.Runs.SingleAsync(r => r.Id == runId);
        if (run.Status is RunStatus.Pending or RunStatus.Running)
        {
            run.Status = RunStatus.Completed;
            run.LeasedBy = null;
            run.LeaseExpiresAt = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task A_retryable_failure_is_retried_with_backoff_until_it_succeeds()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db);

        try
        {
            var handler = new FlakyHandler(StageId.Intake, succeedsOnAttempt: 3);
            var options = new EngineOptions { WorkerId = "retry-test", LeaseDurationSeconds = 30, RetryBackoffBaseSeconds = 0.01, MaxAttempts = 5 };

            await DriveUntilPastStageAsync(fixture.ConnectionString, handler, options, run.Id, StageId.Intake, TimeSpan.FromSeconds(10));

            var checkpoints = await db.StageCheckpoints
                .Where(c => c.RunId == run.Id && c.Stage == StageId.Intake)
                .OrderBy(c => c.Attempt)
                .ToListAsync();

            Assert.Equal(3, checkpoints.Count);
            Assert.Equal([RunStatus.Failed, RunStatus.Failed, RunStatus.Completed], checkpoints.Select(c => c.Status));
            Assert.Equal([1, 2, 3], checkpoints.Select(c => c.Attempt));

            var final = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
            Assert.Equal(StageId.Spec, final.CurrentStage);
            Assert.Equal(RunStatus.Pending, final.Status);

            var failedAttemptEvents = await db.Events.CountAsync(e => e.RunId == run.Id && e.Type == "stage.attempt_failed");
            Assert.Equal(2, failedAttemptEvents);
        }
        finally
        {
            await RetireAsync(fixture.ConnectionString, run.Id);
        }
    }

    [Fact]
    public async Task A_retryable_failure_that_never_succeeds_fails_the_run_after_max_attempts()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db);

        try
        {
            var handler = new AlwaysFailsHandler(StageId.Intake);
            var options = new EngineOptions { WorkerId = "retry-test", LeaseDurationSeconds = 30, RetryBackoffBaseSeconds = 0.01, MaxAttempts = 2 };

            await DriveUntilPastStageAsync(fixture.ConnectionString, handler, options, run.Id, StageId.Intake, TimeSpan.FromSeconds(10));

            var final = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
            Assert.Equal(RunStatus.Failed, final.Status);
            Assert.Equal(StageId.Intake, final.CurrentStage); // never advanced

            var stageFailedEvents = await db.Events.CountAsync(e => e.RunId == run.Id && e.Type == "stage.failed");
            Assert.Equal(1, stageFailedEvents);
        }
        finally
        {
            await RetireAsync(fixture.ConnectionString, run.Id);
        }
    }
}
