using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Creating a run from approved amendments (docs/adr/0003 as amended,
/// docs/adr/0029).
///
/// A run is no longer created from raw text. It is created from
/// specifications a human has approved, against a snapshot taken at that
/// moment — which is what makes "what was this built against?" answerable
/// afterwards, and what stops a run from drifting when the graph moves
/// underneath it mid-flight.
/// </summary>
public sealed class WorkService(DarkFactoryDbContext db, SpecGraphService specs)
{
    /// <summary>
    /// Runs start at <c>plan</c>. Intake and spec are the conversation now
    /// (docs/adr/0003, as amended) — by the time a run exists, the
    /// specification work is already done and approved.
    /// </summary>
    public const StageId StartStage = StageId.Plan;

    public async Task<Run> CreateAsync(
        string projectId,
        IReadOnlyList<string> amendmentIds,
        string actorId,
        CancellationToken cancellationToken = default)
    {
        if (amendmentIds.Count == 0)
        {
            throw new InvalidOperationException(
                "A run is created from approved amendments; none were given (docs/adr/0003, as amended).");
        }

        var project = await db.Projects.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new InvalidOperationException($"No project '{projectId}'.");

        var amendments = await db.Amendments.AsNoTracking()
            .Where(a => a.ProjectId == projectId && amendmentIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        var missing = amendmentIds.Except(amendments.Select(a => a.Id), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"No such amendment(s) in project '{projectId}': {string.Join(", ", missing)}.");
        }

        // docs/adr/0017: only approved amendments can seed a run. Checked
        // here rather than trusted from the caller, because this is the
        // boundary where a proposal becomes something that changes code.
        var unapproved = amendments.Where(a => a.Status != AmendmentStatus.Approved).ToList();
        if (unapproved.Count > 0)
        {
            throw new InvalidOperationException(
                "Only approved amendments can seed a run. Not approved: " +
                string.Join(", ", unapproved.Select(a => $"{a.Id} ({a.Status})")) + ".");
        }

        // The snapshot is taken now, after approval, so it contains the
        // amendments' effects and nothing that lands afterwards.
        var snapshot = await specs.SnapshotAsync(
            projectId, project.OrgId, $"run-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}", actorId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var workItem = new WorkItem
        {
            Id = Ulid.NewUlid(),
            OrgId = project.OrgId,
            ProjectId = projectId,
            Input = DescribeAmendments(amendments),
            CreatedAt = now,
        };
        db.WorkItems.Add(workItem);

        var run = new Run
        {
            Id = Ulid.NewUlid(),
            OrgId = project.OrgId,
            ProjectId = projectId,
            WorkItemId = workItem.Id,
            CurrentStage = StartStage,
            Status = RunStatus.Pending,
            SnapshotId = snapshot.Id,
            AmendmentIds = amendmentIds.ToArray(),
            CreatedAt = now,
        };
        db.Runs.Add(run);

        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>
    /// A human-readable statement of what the run is for, derived from the
    /// amendments rather than from a prompt someone typed — the specs are
    /// the intent now.
    /// </summary>
    private static string DescribeAmendments(IReadOnlyList<Amendment> amendments)
    {
        var lines = new List<string>();
        foreach (var amendment in amendments)
        {
            var diff = JsonSerializer.Deserialize<SpecDiff>(amendment.DiffJson);
            var counts = new List<string>();
            if (diff is not null)
            {
                if (diff.Creates.Count > 0) counts.Add($"{diff.Creates.Count} new");
                if (diff.Revises.Count > 0) counts.Add($"{diff.Revises.Count} revised");
                if (diff.Retires.Count > 0) counts.Add($"{diff.Retires.Count} retired");
            }
            lines.Add($"{amendment.Id}: {(counts.Count > 0 ? string.Join(", ", counts) : "no node changes")}");
        }
        return "Implement approved amendments — " + string.Join("; ", lines);
    }
}
