using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// One revision of a node with the answer to "why" attached — the reason
/// given at the time, and who gave it (docs/adr/0016, as amended).
///
/// The reason lives per revision rather than per node because that is where
/// it is true: a node revised three times has three reasons, and the useful
/// one is almost never the latest.
/// </summary>
public sealed record SpecRevisionSummary(
    string Hash,
    string Text,
    string? Rationale,
    DateTimeOffset CreatedAt,
    string ActorId,
    string? ApprovedBy,
    string? ConversationId,
    string? TurnId);

public sealed record SnapshotDiffResult(
    IReadOnlyList<string> CreatedSpecIds,
    IReadOnlyList<string> RevisedSpecIds,
    IReadOnlyList<string> RetiredSpecIds,
    IReadOnlyList<SpecEdge> EdgesAdded,
    IReadOnlyList<SpecEdge> EdgesRetired);

/// <summary>
/// The spec graph's read/write surface (docs/adr/0016,
/// docs/adr/0017): propose and approve amendments, query and traverse the
/// graph, and diff snapshots. This is the backend the eventual df.specs.*
/// MCP tools (step 3b+) will be a thin wrapper over.
/// </summary>
public sealed class SpecGraphService(DarkFactoryDbContext db)
{
    public static string ComputeHash(string canonicalText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText))).ToLowerInvariant();

    public async Task<Amendment> ProposeAsync(
        string projectId,
        string orgId,
        string conversationId,
        string? turnId,
        string proposedBy,
        SpecDiff diff,
        CancellationToken cancellationToken = default)
    {
        var amendment = new Amendment
        {
            Id = Ulid.NewUlid(),
            ProjectId = projectId,
            OrgId = orgId,
            ConversationId = conversationId,
            TurnId = turnId,
            ProposedBy = proposedBy,
            Status = AmendmentStatus.Proposed,
            DiffJson = JsonSerializer.Serialize(diff),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Amendments.Add(amendment);
        await db.SaveChangesAsync(cancellationToken);
        return amendment;
    }

    /// <summary>
    /// Applies the amendment's diff to the graph: creates/revises nodes,
    /// adds/retires edges, all stamped with one Provenance row for this
    /// approval. Everything the approval touches (the amendment, the new
    /// provenance row, every node/revision/edge write) commits in a single
    /// SaveChangesAsync — an approval either fully lands or doesn't.
    /// </summary>
    public async Task<Amendment> ApproveAsync(string amendmentId, string approvedBy, CancellationToken cancellationToken = default)
    {
        var amendment = await db.Amendments.SingleAsync(a => a.Id == amendmentId, cancellationToken);
        if (amendment.Status != AmendmentStatus.Proposed)
        {
            throw new InvalidOperationException($"Amendment {amendmentId} is not Proposed (currently {amendment.Status}).");
        }

        var diff = JsonSerializer.Deserialize<SpecDiff>(amendment.DiffJson)!;
        var now = DateTimeOffset.UtcNow;

        var provenance = new Provenance
        {
            Id = Ulid.NewUlid(),
            ConversationId = amendment.ConversationId,
            TurnId = amendment.TurnId,
            ActorType = ActorType.Human,
            ActorId = approvedBy,
            ApprovedBy = approvedBy,
            At = now,
        };
        db.Provenance.Add(provenance);

        // A diff may create a node and an edge to it in the same amendment,
        // which it expresses as "new:0" — the node has no spec_id until
        // this moment, because the factory assigns ids, never the proposer.
        // Index → assigned id, filled as creates are applied and consulted
        // when the edges are.
        var createdSpecIds = new List<string>();

        foreach (var create in diff.Creates)
        {
            var specId = Ulid.NewUlid();
            createdSpecIds.Add(specId);
            db.SpecNodes.Add(new SpecNode
            {
                SpecId = specId,
                ProjectId = amendment.ProjectId,
                OrgId = amendment.OrgId,
                Kind = create.Kind,
                Layer = create.Layer,
                CreatedAt = now,
            });
            await AddRevisionIfNewAsync(specId, create.ContentJson, create.CanonicalText, provenance.Id, now, cancellationToken);
        }

        foreach (var revise in diff.Revises)
        {
            await AddRevisionIfNewAsync(revise.SpecId, revise.ContentJson, revise.CanonicalText, provenance.Id, now, cancellationToken);
        }

        foreach (var retire in diff.Retires)
        {
            var node = await db.SpecNodes.SingleAsync(n => n.SpecId == retire.SpecId, cancellationToken);
            node.RetiredAt = now;
        }

        foreach (var edgeAdd in diff.EdgeAdds)
        {
            db.SpecEdges.Add(new SpecEdge
            {
                Id = Ulid.NewUlid(),
                ProjectId = amendment.ProjectId,
                OrgId = amendment.OrgId,
                FromSpecId = ResolveEndpoint(edgeAdd.FromSpecId, createdSpecIds),
                ToSpecId = ResolveEndpoint(edgeAdd.ToSpecId, createdSpecIds),
                Kind = edgeAdd.Kind,
                CreatedAt = now,
                ProvenanceId = provenance.Id,
            });
        }

        foreach (var edgeRetire in diff.EdgeRetires)
        {
            var edge = await db.SpecEdges.SingleAsync(e => e.Id == edgeRetire.EdgeId, cancellationToken);
            edge.RetiredAt = now;
        }

        amendment.Status = AmendmentStatus.Approved;
        await db.SaveChangesAsync(cancellationToken);
        return amendment;
    }

    public async Task<Amendment> RejectAsync(string amendmentId, string rejectedBy, string reason, CancellationToken cancellationToken = default)
    {
        var amendment = await db.Amendments.SingleAsync(a => a.Id == amendmentId, cancellationToken);
        if (amendment.Status != AmendmentStatus.Proposed)
        {
            throw new InvalidOperationException($"Amendment {amendmentId} is not Proposed (currently {amendment.Status}).");
        }

        amendment.Status = AmendmentStatus.Rejected;
        await db.SaveChangesAsync(cancellationToken);
        return amendment;
    }

    /// <summary>
    /// Resolves a "new:N" forward reference to the spec id assigned to the
    /// Nth create in this same amendment. Anything else is already a real
    /// spec id and passes through. Out-of-range indexes are rejected before
    /// an amendment is ever persisted (SpecDiffTranslator), so reaching one
    /// here means the diff was written directly rather than proposed, and
    /// failing loudly beats inventing an endpoint.
    /// </summary>
    private static string ResolveEndpoint(string reference, IReadOnlyList<string> createdSpecIds)
    {
        const string prefix = "new:";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
        {
            return reference;
        }

        if (!int.TryParse(reference[prefix.Length..], out var index) || index >= createdSpecIds.Count)
        {
            throw new InvalidOperationException(
                $"Edge endpoint '{reference}' does not refer to a node this amendment creates " +
                $"(it creates {createdSpecIds.Count}).");
        }

        return createdSpecIds[index];
    }

    /// <summary>Content addressing means proposing identical content for the same node twice is a no-op, not a duplicate.</summary>
    private async Task AddRevisionIfNewAsync(
        string specId, string contentJson, string canonicalText, string provenanceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hash = ComputeHash(canonicalText);
        var exists = await db.SpecRevisions.AnyAsync(r => r.SpecId == specId && r.Hash == hash, cancellationToken);
        if (exists)
        {
            return;
        }

        db.SpecRevisions.Add(new SpecRevision
        {
            SpecId = specId,
            Hash = hash,
            ContentJson = contentJson,
            CanonicalText = canonicalText,
            CreatedAt = now,
            ProvenanceId = provenanceId,
        });
    }

    public Task<SpecNode?> GetNodeAsync(string specId, CancellationToken cancellationToken = default) =>
        db.SpecNodes.AsNoTracking().SingleOrDefaultAsync(n => n.SpecId == specId, cancellationToken);

    public Task<SpecRevision?> GetLatestRevisionAsync(string specId, CancellationToken cancellationToken = default) =>
        db.SpecRevisions.AsNoTracking()
            .Where(r => r.SpecId == specId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Every revision of a node, oldest first, each with its own reason and
    /// provenance. This is what makes an amendment's "why" recoverable long
    /// after the conversation that produced it has scrolled away.
    /// </summary>
    public async Task<IReadOnlyList<SpecRevisionSummary>> RevisionHistoryAsync(
        string specId, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from revision in db.SpecRevisions.AsNoTracking()
            join provenance in db.Provenance.AsNoTracking()
                on revision.ProvenanceId equals provenance.Id
            where revision.SpecId == specId
            orderby revision.CreatedAt
            select new
            {
                revision.Hash,
                revision.CanonicalText,
                revision.ContentJson,
                revision.CreatedAt,
                provenance.ActorId,
                provenance.ApprovedBy,
                provenance.ConversationId,
                provenance.TurnId,
            }).ToListAsync(cancellationToken);

        return rows.Select(r => new SpecRevisionSummary(
            r.Hash,
            r.CanonicalText,
            SpecDiffTranslator.RationaleOf(r.ContentJson),
            r.CreatedAt,
            r.ActorId,
            r.ApprovedBy,
            r.ConversationId,
            r.TurnId)).ToList();
    }

    public Task<SpecRevision?> GetRevisionAsync(string specId, string hash, CancellationToken cancellationToken = default) =>
        db.SpecRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.SpecId == specId && r.Hash == hash, cancellationToken);

    /// <summary>Simple substring search over canonical text — the embedding index (docs/adr/0023) is out of scope for this slice.</summary>
    public async Task<IReadOnlyList<SpecNode>> QueryAsync(
        string projectId, string? q, string? layer, IReadOnlyList<string>? kinds, int limit, CancellationToken cancellationToken = default)
    {
        var nodes = db.SpecNodes.AsNoTracking().Where(n => n.ProjectId == projectId && n.RetiredAt == null);

        if (layer is not null)
        {
            nodes = nodes.Where(n => n.Layer == layer);
        }
        if (kinds is { Count: > 0 })
        {
            nodes = nodes.Where(n => kinds.Contains(n.Kind));
        }

        var candidates = await nodes.OrderByDescending(n => n.CreatedAt).Take(Math.Max(limit * 4, limit)).ToListAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(q))
        {
            return candidates.Take(limit).ToList();
        }

        var matches = new List<SpecNode>();
        foreach (var node in candidates)
        {
            var latest = await GetLatestRevisionAsync(node.SpecId, cancellationToken);
            if (latest is not null && latest.CanonicalText.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(node);
                if (matches.Count >= limit)
                {
                    break;
                }
            }
        }
        return matches;
    }

    public async Task<IReadOnlyList<SpecEdge>> NeighborhoodAsync(string specId, int depth, CancellationToken cancellationToken = default)
    {
        var frontier = new HashSet<string> { specId };
        var visited = new HashSet<string> { specId };
        var edges = new List<SpecEdge>();

        for (var i = 0; i < Math.Max(depth, 0); i++)
        {
            if (frontier.Count == 0)
            {
                break;
            }

            var step = await db.SpecEdges.AsNoTracking()
                .Where(e => e.RetiredAt == null && (frontier.Contains(e.FromSpecId) || frontier.Contains(e.ToSpecId)))
                .ToListAsync(cancellationToken);

            var nextFrontier = new HashSet<string>();
            foreach (var edge in step)
            {
                edges.Add(edge);
                if (visited.Add(edge.FromSpecId))
                {
                    nextFrontier.Add(edge.FromSpecId);
                }
                if (visited.Add(edge.ToSpecId))
                {
                    nextFrontier.Add(edge.ToSpecId);
                }
            }
            frontier = nextFrontier;
        }

        return edges.DistinctBy(e => e.Id).ToList();
    }

    /// <summary>Captures the latest revision of every non-retired node in the project, plus the non-retired edge set.</summary>
    public async Task<SpecSnapshot> SnapshotAsync(string projectId, string orgId, string name, string actorId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        var provenance = new Provenance
        {
            Id = Ulid.NewUlid(),
            ActorType = ActorType.Human,
            ActorId = actorId,
            At = now,
        };
        db.Provenance.Add(provenance);

        var snapshot = new SpecSnapshot
        {
            Id = Ulid.NewUlid(),
            ProjectId = projectId,
            OrgId = orgId,
            Name = name,
            CreatedAt = now,
            ProvenanceId = provenance.Id,
        };
        db.SpecSnapshots.Add(snapshot);

        var activeNodeIds = await db.SpecNodes.AsNoTracking()
            .Where(n => n.ProjectId == projectId && n.RetiredAt == null)
            .Select(n => n.SpecId)
            .ToListAsync(cancellationToken);

        foreach (var specId in activeNodeIds)
        {
            var latest = await GetLatestRevisionAsync(specId, cancellationToken);
            if (latest is null)
            {
                continue;
            }
            db.SnapshotMembers.Add(new SnapshotMember
            {
                SnapshotId = snapshot.Id,
                SpecId = specId,
                RevisionHash = latest.Hash,
            });
        }

        var activeEdgeIds = await db.SpecEdges.AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.RetiredAt == null)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        foreach (var edgeId in activeEdgeIds)
        {
            db.SnapshotEdges.Add(new SnapshotEdge { SnapshotId = snapshot.Id, EdgeId = edgeId });
        }

        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    public async Task<SnapshotDiffResult> DiffAsync(string snapshotAId, string snapshotBId, CancellationToken cancellationToken = default)
    {
        // An empty snapshot and a nonexistent one produce identical member
        // sets, so without this an unknown id would come back as "nothing
        // changed" rather than as an error.
        foreach (var id in new[] { snapshotAId, snapshotBId })
        {
            if (!await db.SpecSnapshots.AsNoTracking().AnyAsync(s => s.Id == id, cancellationToken))
            {
                throw new InvalidOperationException($"Snapshot '{id}' does not exist.");
            }
        }

        var membersA = await db.SnapshotMembers.AsNoTracking().Where(m => m.SnapshotId == snapshotAId).ToListAsync(cancellationToken);
        var membersB = await db.SnapshotMembers.AsNoTracking().Where(m => m.SnapshotId == snapshotBId).ToListAsync(cancellationToken);

        var aBySpec = membersA.ToDictionary(m => m.SpecId, m => m.RevisionHash);
        var bBySpec = membersB.ToDictionary(m => m.SpecId, m => m.RevisionHash);

        var created = bBySpec.Keys.Where(id => !aBySpec.ContainsKey(id)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var retired = aBySpec.Keys.Where(id => !bBySpec.ContainsKey(id)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var revised = aBySpec.Keys.Intersect(bBySpec.Keys)
            .Where(id => aBySpec[id] != bBySpec[id])
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        // Each snapshot recorded its own active edge set (see SnapshotEdge),
        // so this is a set comparison rather than a reconstruction from
        // created_at/retired_at against the snapshots' timestamps. That
        // matters: the timestamp version silently depends on clock
        // resolution, and two snapshots taken within the same tick would
        // report no edge changes between them however much had changed.
        var edgeIdsA = (await db.SnapshotEdges.AsNoTracking()
            .Where(m => m.SnapshotId == snapshotAId).Select(m => m.EdgeId).ToListAsync(cancellationToken)).ToHashSet();
        var edgeIdsB = (await db.SnapshotEdges.AsNoTracking()
            .Where(m => m.SnapshotId == snapshotBId).Select(m => m.EdgeId).ToListAsync(cancellationToken)).ToHashSet();

        var changedEdgeIds = edgeIdsA.Union(edgeIdsB).Except(edgeIdsA.Intersect(edgeIdsB)).ToList();
        var changedEdges = await db.SpecEdges.AsNoTracking()
            .Where(e => changedEdgeIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var edgesAdded = changedEdges.Where(e => edgeIdsB.Contains(e.Id)).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var edgesRetired = changedEdges.Where(e => edgeIdsA.Contains(e.Id)).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();

        return new SnapshotDiffResult(created, revised, retired, edgesAdded, edgesRetired);
    }
}
