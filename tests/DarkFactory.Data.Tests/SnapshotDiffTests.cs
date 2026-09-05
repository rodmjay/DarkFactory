using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0016: snapshots are diffable, and the diff is what the
/// ADR-0021 `spec_diff` component renders and what a run records itself
/// against. Nothing here waits on a clock — snapshots pin their own node
/// revisions and edge set, so a diff is a set comparison (see SnapshotEdge).
/// </summary>
[Collection("SpecGraph")]
public sealed class SnapshotDiffTests(SpecGraphTestFixture fixture)
{
    private async Task<SpecSnapshotIds> BuildGraphAsync()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        // Three nodes, one edge.
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(creates:
        [
            Graph.Create("Accounts have exactly one owner.", layer: "domain"),
            Graph.Create("Owners cannot be removed.", layer: "domain"),
            Graph.Create("Ownership transfer is audited.", layer: "domain"),
        ]));

        List<string> specIds;
        await using (var db = fixture.NewDb())
        {
            specIds = await db.SpecNodes.Where(n => n.ProjectId == projectId)
                .OrderBy(n => n.SpecId).Select(n => n.SpecId).ToListAsync();
        }

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(
            edgeAdds: [new SpecDiffEdgeAdd(specIds[1], specIds[0], "depends_on")]));

        string edgeKept;
        await using (var db = fixture.NewDb())
        {
            edgeKept = await db.SpecEdges.Where(e => e.ProjectId == projectId).Select(e => e.Id).SingleAsync();
        }

        string snapshotA;
        await using (var db = fixture.NewDb())
        {
            snapshotA = (await new SpecGraphService(db).SnapshotAsync(projectId, orgId, "before", "tester")).Id;
        }

        // One create, one revise, one retire, one edge added, one edge retired.
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(
            creates: [Graph.Create("Transfers require two approvals.", layer: "domain")],
            revises: [Graph.Revise(specIds[1], "Owners cannot be removed while a subscription is active.")],
            retires: [new SpecDiffNodeRetire(specIds[2])],
            edgeAdds: [new SpecDiffEdgeAdd(specIds[0], specIds[1], "constrains")],
            edgeRetires: [new SpecDiffEdgeRetire(edgeKept)]));

        string newSpecId, edgeAdded;
        await using (var db = fixture.NewDb())
        {
            newSpecId = await db.SpecNodes.Where(n => n.ProjectId == projectId && !specIds.Contains(n.SpecId))
                .Select(n => n.SpecId).SingleAsync();
            edgeAdded = await db.SpecEdges.Where(e => e.ProjectId == projectId && e.Id != edgeKept)
                .Select(e => e.Id).SingleAsync();
        }

        string snapshotB;
        await using (var db = fixture.NewDb())
        {
            snapshotB = (await new SpecGraphService(db).SnapshotAsync(projectId, orgId, "after", "tester")).Id;
        }

        return new SpecSnapshotIds(snapshotA, snapshotB, specIds[1], specIds[2], newSpecId, edgeAdded, edgeKept);
    }

    private sealed record SpecSnapshotIds(
        string SnapshotA,
        string SnapshotB,
        string RevisedSpecId,
        string RetiredSpecId,
        string CreatedSpecId,
        string AddedEdgeId,
        string RetiredEdgeId);

    [Fact]
    public async Task DiffReportsCreatedRevisedAndRetiredNodesAndEdgeChanges()
    {
        var ids = await BuildGraphAsync();

        await using var db = fixture.NewDb();
        var diff = await new SpecGraphService(db).DiffAsync(ids.SnapshotA, ids.SnapshotB);

        Assert.Equal(ids.CreatedSpecId, Assert.Single(diff.CreatedSpecIds));
        Assert.Equal(ids.RevisedSpecId, Assert.Single(diff.RevisedSpecIds));
        Assert.Equal(ids.RetiredSpecId, Assert.Single(diff.RetiredSpecIds));

        Assert.Equal(ids.AddedEdgeId, Assert.Single(diff.EdgesAdded).Id);
        Assert.Equal(ids.RetiredEdgeId, Assert.Single(diff.EdgesRetired).Id);
    }

    [Fact]
    public async Task DiffOfASnapshotAgainstItselfIsEmpty()
    {
        var ids = await BuildGraphAsync();

        await using var db = fixture.NewDb();
        var diff = await new SpecGraphService(db).DiffAsync(ids.SnapshotB, ids.SnapshotB);

        Assert.Empty(diff.CreatedSpecIds);
        Assert.Empty(diff.RevisedSpecIds);
        Assert.Empty(diff.RetiredSpecIds);
        Assert.Empty(diff.EdgesAdded);
        Assert.Empty(diff.EdgesRetired);
    }

    [Fact]
    public async Task DiffIsDirectional()
    {
        var ids = await BuildGraphAsync();

        await using var db = fixture.NewDb();
        var reversed = await new SpecGraphService(db).DiffAsync(ids.SnapshotB, ids.SnapshotA);

        // Reversing the arguments swaps creates with retires: going
        // backwards, the node that was added is the node that goes away.
        Assert.Equal(ids.RetiredSpecId, Assert.Single(reversed.CreatedSpecIds));
        Assert.Equal(ids.CreatedSpecId, Assert.Single(reversed.RetiredSpecIds));
        Assert.Equal(ids.RevisedSpecId, Assert.Single(reversed.RevisedSpecIds));
        Assert.Equal(ids.RetiredEdgeId, Assert.Single(reversed.EdgesAdded).Id);
        Assert.Equal(ids.AddedEdgeId, Assert.Single(reversed.EdgesRetired).Id);
    }

    [Fact]
    public async Task ASnapshotIsImmutableEvenAsTheGraphMovesOn()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create("Original text.")]));

        string specId, snapshotId, originalHash;
        await using (var db = fixture.NewDb())
        {
            specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
            originalHash = await db.SpecRevisions.Where(r => r.SpecId == specId).Select(r => r.Hash).SingleAsync();
            snapshotId = (await new SpecGraphService(db).SnapshotAsync(projectId, orgId, "pinned", "tester")).Id;
        }

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(revises: [Graph.Revise(specId, "Rewritten text.")]));

        await using (var db = fixture.NewDb())
        {
            // The snapshot still points at the revision it captured. This
            // is the property a run depends on: "what was true when this
            // was built" cannot be changed after the fact.
            var member = await db.SnapshotMembers.AsNoTracking()
                .SingleAsync(m => m.SnapshotId == snapshotId && m.SpecId == specId);
            Assert.Equal(originalHash, member.RevisionHash);

            var pinned = await new SpecGraphService(db).GetRevisionAsync(specId, member.RevisionHash);
            Assert.Equal("Original text.", pinned!.CanonicalText);
        }
    }

    [Fact]
    public async Task DiffingAnUnknownSnapshotFailsRatherThanReportingNoChanges()
    {
        var ids = await BuildGraphAsync();

        await using var db = fixture.NewDb();
        var service = new SpecGraphService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DiffAsync(ids.SnapshotA, "no_such_snapshot"));
    }
}
