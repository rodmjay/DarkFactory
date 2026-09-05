using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Assembles what a run stage's agent sees, and persists it by reference —
/// the same discipline as a conversational turn's <see cref="ContextPack"/>,
/// for the same reason.
///
/// The specs come from the run's <em>snapshot</em>, not from the live graph.
/// That is the point of recording a snapshot at all: an implement stage that
/// re-queried the graph would build against whatever landed while it was
/// planning, and "what was this built against?" would have no answer.
/// </summary>
public sealed class StageContextBuilder(
    DarkFactoryDbContext db,
    IArtifactStore artifacts,
    TeamService teams,
    RunObservationService observation)
{
    public const string StageContextArtifactType = "StageContextPack";

    public sealed record BuiltStageContext(StageContextPack Pack, string Ref, ResolvedAgent Agent);

    public async Task<BuiltStageContext> BuildAsync(
        Run run, StageId stage, int attempt, CancellationToken cancellationToken = default)
    {
        var point = stage switch
        {
            StageId.Plan => AssignmentPoints.Plan,
            StageId.Implement => AssignmentPoints.Implement,
            StageId.Verify => AssignmentPoints.Verify,
            StageId.Ship => AssignmentPoints.Ship,
            _ => AssignmentPoints.Triage,
        };

        var agent = await teams.ResolveAsync(run.ProjectId, point, cancellationToken);

        var (specs, edges) = await SpecsFromSnapshotAsync(run, cancellationToken);
        var steers = await observation.SteersForAsync(run.Id, cancellationToken);
        var prior = await PriorArtifactsAsync(run.Id, cancellationToken);

        var pack = new StageContextPack
        {
            RunId = run.Id,
            ProjectId = run.ProjectId,
            Stage = stage.ToString(),
            Attempt = attempt,
            Agent = new ContextAgent(agent.Role, agent.Deployment, agent.TeamId, agent.TeamMemberId),
            SnapshotId = run.SnapshotId,
            Specs = specs,
            SpecEdges = edges,
            Standards = [ArchitectPrompt.DefaultStandards],
            Skills = [],
            Steers = steers,
            PriorArtifacts = prior,
            AssembledAt = DateTimeOffset.UtcNow,
        };

        var artifact = await artifacts.PutAsync(
            run.OrgId, run.ProjectId, run.Id, StageContextArtifactType,
            JsonSerializer.Serialize(pack), ArtifactContentTypes.Json, cancellationToken);

        return new BuiltStageContext(pack, ArtifactRef.Format(artifact.Id), agent);
    }

    /// <summary>
    /// Reads the run's pinned snapshot: the exact revision of each node as
    /// it was when the run was created, not the latest.
    /// </summary>
    private async Task<(IReadOnlyList<ContextSpecNode>, IReadOnlyList<ContextSpecEdge>)> SpecsFromSnapshotAsync(
        Run run, CancellationToken cancellationToken)
    {
        if (run.SnapshotId is null)
        {
            return ([], []);
        }

        var members = await db.SnapshotMembers.AsNoTracking()
            .Where(m => m.SnapshotId == run.SnapshotId)
            .ToListAsync(cancellationToken);

        var specIds = members.Select(m => m.SpecId).ToList();

        var nodes = (await db.SpecNodes.AsNoTracking()
            .Where(n => specIds.Contains(n.SpecId))
            .ToListAsync(cancellationToken))
            .ToDictionary(n => n.SpecId);

        var hashes = members.Select(m => m.RevisionHash).ToList();
        var revisions = (await db.SpecRevisions.AsNoTracking()
            .Where(r => specIds.Contains(r.SpecId) && hashes.Contains(r.Hash))
            .ToListAsync(cancellationToken))
            .ToDictionary(r => (r.SpecId, r.Hash));

        var specs = new List<ContextSpecNode>();
        foreach (var member in members.OrderBy(m => m.SpecId, StringComparer.Ordinal))
        {
            if (nodes.TryGetValue(member.SpecId, out var node)
                && revisions.TryGetValue((member.SpecId, member.RevisionHash), out var revision))
            {
                specs.Add(new ContextSpecNode(
                    node.SpecId, node.Kind, node.Layer, revision.Hash, revision.CanonicalText,
                    node.RetiredAt is not null));
            }
        }

        var edgeIds = await db.SnapshotEdges.AsNoTracking()
            .Where(e => e.SnapshotId == run.SnapshotId)
            .Select(e => e.EdgeId)
            .ToListAsync(cancellationToken);

        var edges = await db.SpecEdges.AsNoTracking()
            .Where(e => edgeIds.Contains(e.Id))
            .Select(e => new ContextSpecEdge(e.Id, e.FromSpecId, e.ToSpecId, e.Kind))
            .ToListAsync(cancellationToken);

        return (specs, edges);
    }

    /// <summary>
    /// What earlier stages produced, by ref. Context packs are excluded —
    /// a stage does not need to read what a previous stage was shown, and
    /// including them would nest the record inside itself.
    /// </summary>
    private async Task<IReadOnlyList<ContextArtifact>> PriorArtifactsAsync(
        string runId, CancellationToken cancellationToken)
    {
        var checkpoints = await db.StageCheckpoints.AsNoTracking()
            .Where(c => c.RunId == runId && c.ArtifactRef != null)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var results = new List<ContextArtifact>();
        foreach (var checkpoint in checkpoints)
        {
            var artifact = await artifacts.GetAsync(checkpoint.ArtifactRef!, cancellationToken);
            if (artifact is null || artifact.Type == StageContextArtifactType)
            {
                continue;
            }
            results.Add(new ContextArtifact(artifact.Type, checkpoint.ArtifactRef!, checkpoint.Stage.ToString()));
        }
        return results;
    }
}
