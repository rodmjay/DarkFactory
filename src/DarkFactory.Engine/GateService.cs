using System.Text.Json;
using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine;

/// <summary>
/// Resolves a gate (docs/adr/0003's two planned v1 gates, or an unplanned
/// docs/adr/0007 needs_human escalation reusing the same mechanism).
/// Approving a planned gate advances the run to the next pipeline stage and
/// makes it reclaimable again; rejecting one fails the run. This lives
/// alongside the state machine because it mutates the same run/lease
/// invariants — the front MCP surface's work.approve/work.reject (step 3)
/// will be a thin wrapper over it.
/// </summary>
public sealed class GateService(DarkFactoryDbContext dbContext)
{
    public async Task<Gate> ApproveAsync(string runId, GateKind kind, CancellationToken cancellationToken = default)
    {
        var run = await dbContext.Runs.SingleAsync(r => r.Id == runId, cancellationToken);
        var gate = await WaitingGateAsync(runId, kind, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        gate.Status = GateStatus.Approved;
        gate.ResolvedAt = now;

        if (kind == GateKind.NeedsHuman)
        {
            // An unplanned escalation resumes the same stage rather than
            // advancing — the run wasn't done with that stage, it was stuck.
            run.Status = RunStatus.Pending;
        }
        else
        {
            run.CurrentStage = PipelineStages.Next(run.CurrentStage);
            run.Status = RunStatus.Pending;
        }

        run.UpdatedAt = now;

        dbContext.Events.Add(new Event
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = runId,
            Type = "gate.approved",
            DataJson = JsonSerializer.Serialize(new { gate = kind.ToString(), next_stage = run.CurrentStage.ToString() }),
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return gate;
    }

    public async Task<Gate> RejectAsync(string runId, GateKind kind, string reason, CancellationToken cancellationToken = default)
    {
        var run = await dbContext.Runs.SingleAsync(r => r.Id == runId, cancellationToken);
        var gate = await WaitingGateAsync(runId, kind, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        gate.Status = GateStatus.Rejected;
        gate.Reason = reason;
        gate.ResolvedAt = now;

        run.Status = RunStatus.Failed;
        run.UpdatedAt = now;

        dbContext.Events.Add(new Event
        {
            Id = Guid.NewGuid().ToString("n"),
            RunId = runId,
            Type = "gate.rejected",
            DataJson = JsonSerializer.Serialize(new { gate = kind.ToString(), reason }),
            CreatedAt = now,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return gate;
    }

    private async Task<Gate> WaitingGateAsync(string runId, GateKind kind, CancellationToken cancellationToken)
    {
        return await dbContext.Gates
            .Where(g => g.RunId == runId && g.Kind == kind && g.Status == GateStatus.Waiting)
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"No waiting {kind} gate for run {runId}.");
    }
}
