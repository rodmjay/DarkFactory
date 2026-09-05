using System.ComponentModel;
using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using DarkFactory.Data;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// The spec graph surface (docs/adr/0016, docs/adr/0017). Everything here
/// is a thin wrapper over <see cref="SpecGraphService"/> — content
/// addressing, append-only enforcement, snapshots and diff all live there.
/// </summary>
[McpServerToolType]
public static class SpecTools
{
    [McpServerTool(Name = "df.specs.query"),
     Description("Search a project's specifications by text, optionally filtered by layer and kind.")]
    public static async Task<IReadOnlyList<SpecNodeSummary>> Query(
        SpecGraphService specs,
        [Description("The project to search.")] string project_id,
        [Description("Substring to match against specification text. Omit to list.")] string? q = null,
        [Description("Restrict to one architectural layer.")] string? layer = null,
        [Description("Restrict to these node kinds.")] string[]? kinds = null,
        [Description("Maximum results (default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var nodes = await specs.QueryAsync(project_id, q, layer, kinds, limit, cancellationToken);

        var results = new List<SpecNodeSummary>(nodes.Count);
        foreach (var node in nodes)
        {
            var latest = await specs.GetLatestRevisionAsync(node.SpecId, cancellationToken);
            results.Add(SpecNodeSummary.From(node, latest));
        }
        return results;
    }

    [McpServerTool(Name = "df.specs.get"),
     Description("Get one specification node with its revision history — each revision's text, why it was made, and by whom.")]
    public static async Task<SpecNodeDetail> Get(
        SpecGraphService specs,
        [Description("The node's stable spec id.")] string spec_id,
        [Description("A specific revision hash. Omit for the latest.")] string? revision = null,
        CancellationToken cancellationToken = default)
    {
        var node = await specs.GetNodeAsync(spec_id, cancellationToken)
            ?? throw new ModelContextProtocol.McpException($"No specification node '{spec_id}'.");

        var wanted = revision is null
            ? await specs.GetLatestRevisionAsync(spec_id, cancellationToken)
            : await specs.GetRevisionAsync(spec_id, revision, cancellationToken);

        // The history, not just the current text. "Why does this say what it
        // says" is rarely answered by the latest revision alone — the
        // interesting answer is usually in the one that changed it
        // (docs/adr/0016, as amended).
        var history = await specs.RevisionHistoryAsync(spec_id, cancellationToken);

        return new SpecNodeDetail(SpecNodeSummary.From(node, wanted), history);
    }

    [McpServerTool(Name = "df.specs.neighborhood"),
     Description("Traverse the graph outward from a node, returning the edges found.")]
    public static async Task<IReadOnlyList<EdgeSummary>> Neighborhood(
        SpecGraphService specs,
        [Description("The node to start from.")] string spec_id,
        [Description("How many hops (default 1).")] int depth = 1,
        CancellationToken cancellationToken = default)
    {
        var edges = await specs.NeighborhoodAsync(spec_id, depth, cancellationToken);
        return edges.Select(EdgeSummary.From).ToList();
    }

    [McpServerTool(Name = "df.specs.approve"),
     Description("Approve a proposed amendment, applying it to the graph. Only approved amendments can seed a run.")]
    public static async Task<AmendmentSummary> Approve(
        SpecGraphService specs,
        IConfiguration configuration,
        [Description("The amendment id from df.conversations.turn.")] string amendment_id,
        CancellationToken cancellationToken = default)
    {
        var amendment = await Errors.Surfacing(() => specs.ApproveAsync(
            amendment_id, ServerTools.ResolveOrgId(configuration), cancellationToken));
        return AmendmentSummary.From(amendment);
    }

    [McpServerTool(Name = "df.specs.reject"),
     Description("Reject a proposed amendment.")]
    public static async Task<AmendmentSummary> Reject(
        SpecGraphService specs,
        IConfiguration configuration,
        [Description("The amendment id.")] string amendment_id,
        [Description("Why it was rejected.")] string reason,
        CancellationToken cancellationToken = default)
    {
        var amendment = await Errors.Surfacing(() => specs.RejectAsync(
            amendment_id, ServerTools.ResolveOrgId(configuration), reason, cancellationToken));
        return AmendmentSummary.From(amendment);
    }

    [McpServerTool(Name = "df.specs.snapshot"),
     Description("Take a named, immutable snapshot of a project's specifications.")]
    public static async Task<SnapshotSummary> Snapshot(
        SpecGraphService specs,
        DarkFactoryDbContext db,
        IConfiguration configuration,
        [Description("The project to snapshot.")] string project_id,
        [Description("A name for the snapshot.")] string name,
        CancellationToken cancellationToken = default)
    {
        var orgId = ServerTools.ResolveOrgId(configuration);
        var snapshot = await Errors.Surfacing(() => specs.SnapshotAsync(
            project_id, orgId, name, orgId, cancellationToken));

        return new SnapshotSummary(snapshot.Id, snapshot.ProjectId, snapshot.Name, snapshot.CreatedAt);
    }

    [McpServerTool(Name = "df.specs.diff"),
     Description("Compare two snapshots: nodes created, revised and retired, plus edge changes.")]
    public static async Task<SnapshotDiffSummary> Diff(
        SpecGraphService specs,
        [Description("The earlier snapshot.")] string snapshot_a,
        [Description("The later snapshot.")] string snapshot_b,
        CancellationToken cancellationToken = default)
    {
        var diff = await Errors.Surfacing(() => specs.DiffAsync(snapshot_a, snapshot_b, cancellationToken));

        return new SnapshotDiffSummary(
            diff.CreatedSpecIds,
            diff.RevisedSpecIds,
            diff.RetiredSpecIds,
            diff.EdgesAdded.Select(EdgeSummary.From).ToList(),
            diff.EdgesRetired.Select(EdgeSummary.From).ToList());
    }
}

public sealed record SpecNodeSummary(
    string SpecId, string Kind, string Layer, string? RevisionHash, string? Text, bool Retired)
{
    public static SpecNodeSummary From(SpecNode node, SpecRevision? revision) => new(
        node.SpecId, node.Kind, node.Layer, revision?.Hash, revision?.CanonicalText, node.RetiredAt is not null);
}

/// <param name="Revisions">Oldest first, so a node reads as a history rather than a snapshot.</param>
public sealed record SpecNodeDetail(SpecNodeSummary Node, IReadOnlyList<SpecRevisionSummary> Revisions);

public sealed record EdgeSummary(string EdgeId, string FromSpecId, string ToSpecId, string Kind, bool Retired)
{
    public static EdgeSummary From(SpecEdge edge) =>
        new(edge.Id, edge.FromSpecId, edge.ToSpecId, edge.Kind, edge.RetiredAt is not null);
}

public sealed record AmendmentSummary(string Id, string ProjectId, string Status, SpecDiffDocument? Diff)
{
    public static AmendmentSummary From(Amendment amendment)
    {
        // The stored diff is the internal shape (canonical text + content);
        // the render payload is the wire shape. Convert rather than leak
        // canonical_text into a surface that is supposed to show a human
        // what changed.
        var internalDiff = JsonSerializer.Deserialize<SpecDiff>(amendment.DiffJson);
        var document = internalDiff is null ? null : new SpecDiffDocument
        {
            // Rationale round-trips: what the proposer said about each
            // change comes back on the element it was said about
            // (docs/adr/0016, as amended). It was being dropped here, so an
            // approver read "creates 1" with no answer to "why".
            Creates = internalDiff.Creates
                .Select(c => new SpecDiffCreate
                {
                    Kind = c.Kind, Layer = c.Layer, Text = c.CanonicalText,
                    Rationale = c.Rationale ?? SpecDiffTranslator.RationaleOf(c.ContentJson),
                })
                .ToList(),
            Revises = internalDiff.Revises
                .Select(r => new SpecDiffRevise
                {
                    SpecId = r.SpecId, Text = r.CanonicalText,
                    Rationale = r.Rationale ?? SpecDiffTranslator.RationaleOf(r.ContentJson),
                })
                .ToList(),
            Retires = internalDiff.Retires
                .Select(r => new SpecDiffRetire { SpecId = r.SpecId, Rationale = r.Rationale })
                .ToList(),
            EdgeAdds = internalDiff.EdgeAdds
                .Select(e => new SpecDiffEdgeAddDocument
                {
                    FromSpecId = e.FromSpecId, ToSpecId = e.ToSpecId, Kind = e.Kind,
                    Rationale = e.Rationale,
                })
                .ToList(),
            EdgeRetires = internalDiff.EdgeRetires
                .Select(e => new SpecDiffEdgeRetireDocument { EdgeId = e.EdgeId, Rationale = e.Rationale })
                .ToList(),
        };

        return new AmendmentSummary(amendment.Id, amendment.ProjectId, amendment.Status.ToString(), document);
    }
}

public sealed record SnapshotSummary(string Id, string ProjectId, string Name, DateTimeOffset CreatedAt);

public sealed record SnapshotDiffSummary(
    IReadOnlyList<string> Created,
    IReadOnlyList<string> Revised,
    IReadOnlyList<string> Retired,
    IReadOnlyList<EdgeSummary> EdgesAdded,
    IReadOnlyList<EdgeSummary> EdgesRetired);
