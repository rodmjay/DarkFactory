using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

public sealed record RunEventView(string Id, string Type, string? DataJson, DateTimeOffset CreatedAt);

public sealed record RunView(
    string RunId,
    string ProjectId,
    string Stage,
    string Status,
    string? SnapshotId,
    int TokensUsed,
    IReadOnlyList<RunEventView> Events);

/// <summary>
/// docs/adr/0015: runs are observable and steerable by any authorized
/// client. This is the shape of that, in place before the UI needs it —
/// <c>attach</c> returns the event tail rather than a live stream, and
/// <c>steer</c> records the guidance rather than interrupting a stage.
///
/// Both are deliberately built on the existing event log rather than on
/// anything new. A steer that lived outside the run's durable log would be
/// invisible to the dashboard, absent from the audit trail, and lost on a
/// crash — and the whole point of steering is that it becomes part of the
/// record of why a run did what it did.
/// </summary>
public sealed class RunObservationService(DarkFactoryDbContext db, BudgetService budgets)
{
    public const string SteerEventType = "run.steered";

    /// <summary>How many events the stub returns. A real stream replaces this in step 4.</summary>
    public const int TailSize = 50;

    public async Task<RunView> AttachAsync(string runId, CancellationToken cancellationToken = default)
    {
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"No run '{runId}'.");

        var events = await db.Events.AsNoTracking()
            .Where(e => e.RunId == runId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(TailSize)
            .ToListAsync(cancellationToken);

        return new RunView(
            run.Id,
            run.ProjectId,
            run.CurrentStage.ToString(),
            run.Status.ToString(),
            run.SnapshotId,
            await budgets.TotalForRunAsync(runId, cancellationToken),
            events.OrderBy(e => e.CreatedAt)
                .Select(e => new RunEventView(e.Id, e.Type, e.DataJson, e.CreatedAt))
                .ToList());
    }

    /// <summary>
    /// Injects guidance for the next agent turn without cancelling the run
    /// (docs/adr/0015). Recorded as an event, which is what makes it both
    /// durable and visible.
    /// </summary>
    public async Task<RunEventView> SteerAsync(
        string runId, string message, string actorId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("A steer needs a message.");
        }

        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"No run '{runId}'.");

        if (run.Status is RunStatus.Completed or RunStatus.Cancelled or RunStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Run '{runId}' is {run.Status}; there is no next agent turn to steer.");
        }

        var steer = new Event
        {
            Id = Ulid.NewUlid(),
            RunId = runId,
            Type = SteerEventType,
            DataJson = JsonSerializer.Serialize(new SteerPayload(message, actorId, run.CurrentStage.ToString())),
            CreatedAt = DateTimeOffset.UtcNow,
            // PublishedAt left null: the outbox publisher broadcasts it to
            // the dashboard like any other event (docs/adr/0008).
        };

        db.Events.Add(steer);
        await db.SaveChangesAsync(cancellationToken);

        return new RunEventView(steer.Id, steer.Type, steer.DataJson, steer.CreatedAt);
    }

    /// <summary>
    /// The steers a stage should carry into its context, oldest first.
    /// Everything recorded so far on the run: a steer given during `plan`
    /// is still guidance during `implement`, and silently dropping it at a
    /// stage boundary would make steering feel arbitrary.
    /// </summary>
    public async Task<IReadOnlyList<ContextSteer>> SteersForAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        var events = await db.Events.AsNoTracking()
            .Where(e => e.RunId == runId && e.Type == SteerEventType)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return events
            .Select(e => JsonSerializer.Deserialize<SteerPayload>(e.DataJson!))
            .Where(p => p is not null)
            .Select(p => new ContextSteer(p!.Message, p.ActorId, p.Stage))
            .ToList();
    }

    private sealed record SteerPayload(string Message, string ActorId, string Stage);
}
