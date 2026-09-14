using System.Text.Json;
using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DarkFactory.Engine;

/// <summary>
/// Drives one run through exactly one stage per call
/// (docs/adr/0008-durable-orchestration.md): claim (lease, not just lock —
/// see RunLeaseStore), run the stage handler, then persist the checkpoint,
/// the outbox event, and the run's advanced state in a single
/// SaveChangesAsync — one transaction, so a crash can never land between
/// "the stage finished" and "the world was told."
/// </summary>
public sealed class RunStateMachine(
    DarkFactoryDbContext dbContext,
    RunLeaseStore leaseStore,
    IArtifactStore artifactStore,
    IEnumerable<IStageHandler> stageHandlers,
    IOptions<EngineOptions> options,
    ILogger<RunStateMachine> logger)
{
    private readonly IReadOnlyDictionary<StageId, IStageHandler> _handlers =
        stageHandlers.ToDictionary(h => h.Stage);

    /// <summary>
    /// Claims and processes at most one run's current stage. Returns the
    /// stage that was attempted, or null if nothing was claimable.
    /// </summary>
    public async Task<StageId?> TryProcessOneAsync(CancellationToken cancellationToken = default)
    {
        var run = await leaseStore.ClaimNextRunnableAsync(options.Value.WorkerId, options.Value.LeaseDuration, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var stage = run.CurrentStage;
        var workItem = await dbContext.WorkItems.SingleAsync(w => w.Id == run.WorkItemId, cancellationToken);

        if (!_handlers.TryGetValue(stage, out var handler))
        {
            throw new InvalidOperationException($"No stage handler registered for '{stage}'.");
        }

        // docs/adr/0038: a stage whose workspace is unreachable is not
        // attempted. Attempting it would fail, the failure would count, and
        // a server restart would cost a run its retries — or the run itself.
        // It waits instead, and the health monitor is what ends the wait.
        var unreachable = await UnreachableWorkspaceAsync(run, cancellationToken);
        if (unreachable is not null)
        {
            await WaitForServerAsync(run, stage, unreachable, cancellationToken);
            return stage;
        }

        StageOutcome outcome;
        try
        {
            outcome = await handler.ExecuteAsync(new StageContext(run, workItem, dbContext), cancellationToken);
        }
        catch (Exception ex)
        {
            // An unhandled exception from a stage handler is a bug, not a
            // classified failure — treat it as permanent so it fails loudly
            // rather than retrying blindly against an unknown error.
            logger.LogError(ex, "Stage handler for {Stage} on run {RunId} threw.", stage, run.Id);
            outcome = new StageOutcome.Failed(FailureClass.Permanent, ex.Message);
        }

        var previousAttempts = await dbContext.StageCheckpoints
            .CountAsync(c => c.RunId == run.Id && c.Stage == stage, cancellationToken);
        var attempt = previousAttempts + 1;

        switch (outcome)
        {
            case StageOutcome.Success success:
                await ApplySuccessAsync(run, stage, attempt, success, cancellationToken);
                break;
            case StageOutcome.Failed failed:
                await ApplyFailureAsync(run, stage, attempt, failed, cancellationToken);
                break;
        }

        return stage;
    }

    private async Task ApplySuccessAsync(Run run, StageId stage, int attempt, StageOutcome.Success success, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        string? artifactRef = null;
        if (success.ArtifactType is not null)
        {
            var artifact = await artifactStore.PutAsync(
                run.OrgId, run.ProjectId, run.Id, success.ArtifactType, success.ArtifactContentJson!,
                success.ArtifactContentType, cancellationToken);
            artifactRef = ArtifactRef.Format(artifact.Id);
        }

        dbContext.StageCheckpoints.Add(new StageCheckpoint
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = run.Id,
            Stage = stage,
            Status = RunStatus.Completed,
            ArtifactRef = artifactRef,
            Attempt = attempt,
            CreatedAt = now,
        });

        // Same SaveChanges as the run update below: the outbox event commits
        // atomically with the state it describes (docs/adr/0008).
        dbContext.Events.Add(new Event
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = run.Id,
            Type = "stage.completed",
            DataJson = JsonSerializer.Serialize(new { stage = stage.ToString(), artifact_ref = artifactRef }),
            CreatedAt = now,
        });

        if (success.RequiresGate is { } gateKind)
        {
            dbContext.Gates.Add(new Gate
            {
                Id = Guid.NewGuid().ToString("n"),
                RunId = run.Id,
                Kind = gateKind,
                Status = GateStatus.Waiting,
                CreatedAt = now,
            });
            dbContext.Events.Add(new Event
            {
                Id = Guid.NewGuid().ToString("n"),
                RunId = run.Id,
                Type = "gate.waiting",
                DataJson = JsonSerializer.Serialize(new { gate = gateKind.ToString(), stage = stage.ToString() }),
                CreatedAt = now,
            });
            run.Status = RunStatus.AwaitingApproval;
            // CurrentStage deliberately unchanged: the run is still "at"
            // this stage until a human approves moving past it.
        }
        else if (PipelineStages.IsLast(stage))
        {
            run.Status = RunStatus.Completed;
        }
        else
        {
            run.CurrentStage = PipelineStages.Next(stage);
            run.Status = RunStatus.Pending;
        }

        run.LeasedBy = null;
        run.LeaseExpiresAt = null;
        run.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyFailureAsync(Run run, StageId stage, int attempt, StageOutcome.Failed failed, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        dbContext.StageCheckpoints.Add(new StageCheckpoint
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = run.Id,
            Stage = stage,
            Status = RunStatus.Failed,
            Attempt = attempt,
            CreatedAt = now,
        });

        dbContext.Events.Add(new Event
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = run.Id,
            Type = "stage.attempt_failed",
            DataJson = JsonSerializer.Serialize(new
            {
                stage = stage.ToString(),
                attempt,
                failure_class = failed.FailureClass.ToString(),
                message = failed.Message,
            }),
            CreatedAt = now,
        });

        // docs/adr/0007: the engine acts on the failure class; handlers
        // never implement their own retry loops.
        switch (failed.FailureClass)
        {
            case FailureClass.Retryable when attempt < options.Value.MaxAttempts:
                // Stays "claimed" (Running) but not reclaimable until the
                // backoff elapses — reusing the lease as the backoff clock
                // means a different worker is free to pick it up once it
                // does, same as any other lease expiry.
                run.Status = RunStatus.Running;
                run.LeaseExpiresAt = now + options.Value.Backoff(attempt);
                break;

            case FailureClass.Retryable: // attempts exhausted
                run.Status = RunStatus.Failed;
                run.LeasedBy = null;
                run.LeaseExpiresAt = null;
                dbContext.Events.Add(StageFailedEvent(run.Id, stage, failed, now));
                break;

            case FailureClass.Permanent:
                run.Status = RunStatus.Failed;
                run.LeasedBy = null;
                run.LeaseExpiresAt = null;
                dbContext.Events.Add(StageFailedEvent(run.Id, stage, failed, now));
                break;

            case FailureClass.NeedsHuman:
                dbContext.Gates.Add(new Gate
                {
                    Id = Guid.NewGuid().ToString("n"),
                    RunId = run.Id,
                    Kind = GateKind.NeedsHuman,
                    Status = GateStatus.Waiting,
                    Reason = failed.Message,
                    CreatedAt = now,
                });
                dbContext.Events.Add(new Event
                {
                    Id = Guid.NewGuid().ToString("n"),
                    RunId = run.Id,
                    Type = "gate.waiting",
                    DataJson = JsonSerializer.Serialize(new { gate = "NeedsHuman", stage = stage.ToString(), message = failed.Message }),
                    CreatedAt = now,
                });
                run.Status = RunStatus.AwaitingApproval;
                run.LeasedBy = null;
                run.LeaseExpiresAt = null;
                break;
        }

        run.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task<Server?> UnreachableWorkspaceAsync(Run run, CancellationToken cancellationToken) =>
        (from project in dbContext.Projects.AsNoTracking()
         join server in dbContext.Servers.AsNoTracking()
             on new { project.OrgId, Url = project.WorkspaceMcpUrl } equals new { server.OrgId, server.Url }
         where project.Id == run.ProjectId
             && server.RemovedAt == null
             && server.Status == ServerStatus.Unreachable
         select server).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Holds the run until the server's next check. The lease is the clock,
    /// exactly as it is for retry backoff: the run stays claimed and becomes
    /// claimable again when the monitor is next due to look. No checkpoint is
    /// written, because the stage never ran — an attempt it did not make
    /// must not count against it.
    /// </summary>
    private async Task WaitForServerAsync(Run run, StageId stage, Server server, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        run.Status = RunStatus.Running;
        run.LeaseExpiresAt = server.NextCheckAt is { } next && next > now
            ? next
            : now + ServerHealthService.HealthyInterval;
        run.UpdatedAt = now;

        // Said once per wait, not once per look: a run held through a
        // five-minute outage would otherwise post a dozen identical events.
        var lastEvent = await dbContext.Events.AsNoTracking()
            .Where(e => e.RunId == run.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.Type)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastEvent != "run.waiting_for_server")
        {
            dbContext.Events.Add(new Event
            {
                Id = Guid.NewGuid().ToString("n"),
                RunId = run.Id,
                Type = "run.waiting_for_server",
                DataJson = JsonSerializer.Serialize(new
                {
                    stage = stage.ToString(),
                    server = server.Name,
                    url = server.Url,
                    unreachable_since = server.UnreachableSince,
                    last_error = server.LastError,
                }),
                CreatedAt = now,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Run {RunId} is waiting at {Stage}: workspace server {Server} is unreachable.",
            run.Id, stage, server.Name);
    }

    private static Event StageFailedEvent(string runId, StageId stage, StageOutcome.Failed failed, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        RunId = runId,
        Type = "stage.failed",
        DataJson = JsonSerializer.Serialize(new { stage = stage.ToString(), failure_class = failed.FailureClass.ToString(), message = failed.Message }),
        CreatedAt = now,
    };
}
