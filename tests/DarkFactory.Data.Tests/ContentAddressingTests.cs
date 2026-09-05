using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0016: identical content is the same revision. Content
/// addressing is what makes "has this actually changed?" answerable without
/// diffing text, so the hash has to be a pure function of canonical content
/// and nothing else — not of time, not of who proposed it, not of which
/// amendment carried it.
/// </summary>
[Collection("SpecGraph")]
public sealed class ContentAddressingTests(SpecGraphTestFixture fixture)
{
    [Fact]
    public void IdenticalContentYieldsIdenticalHash()
    {
        const string text = "Renewal is blocked if the account is delinquent.";

        Assert.Equal(SpecGraphService.ComputeHash(text), SpecGraphService.ComputeHash(text));
        Assert.NotEqual(SpecGraphService.ComputeHash(text), SpecGraphService.ComputeHash(text + "."));

        // SHA-256, lowercase hex — the ref format is part of the contract,
        // not an implementation detail, because it ends up in snapshots.
        Assert.Equal(64, SpecGraphService.ComputeHash(text).Length);
        Assert.Matches("^[0-9a-f]{64}$", SpecGraphService.ComputeHash(text));
    }

    [Fact]
    public async Task IdenticalContentProposedTwiceIsOneRevision()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        const string text = "Invoices are immutable once issued.";

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create(text)]));

        string specId;
        await using (var db = fixture.NewDb())
        {
            specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
        }

        // Re-proposing byte-identical content for the same node: content
        // addressing means this is a no-op, not a second revision.
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(revises: [Graph.Revise(specId, text)]));

        await using (var db = fixture.NewDb())
        {
            var revisions = await db.SpecRevisions.AsNoTracking().Where(r => r.SpecId == specId).ToListAsync();
            Assert.Single(revisions);
            Assert.Equal(SpecGraphService.ComputeHash(Graph.Canonical(text)), revisions[0].Hash);
        }
    }

    [Fact]
    public async Task DifferentContentForTheSameNodeAddsARevisionWithoutTouchingTheFirst()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create("Trials last 14 days.")]));

        string specId;
        SpecRevision original;
        await using (var db = fixture.NewDb())
        {
            specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
            original = await db.SpecRevisions.AsNoTracking().SingleAsync(r => r.SpecId == specId);
        }

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(revises: [Graph.Revise(specId, "Trials last 30 days.")]));

        await using (var verify = fixture.NewDb())
        {
            var revisions = await verify.SpecRevisions.AsNoTracking().Where(r => r.SpecId == specId).ToListAsync();
            Assert.Equal(2, revisions.Count);

            // The original revision is byte-for-byte what it was: revising
            // a node adds history, it does not rewrite it.
            var stillThere = revisions.Single(r => r.Hash == original.Hash);
            Assert.Equal(original.CanonicalText, stillThere.CanonicalText);
            Assert.Equal(original.ContentJson, stillThere.ContentJson);
            Assert.Equal(original.ProvenanceId, stillThere.ProvenanceId);
            Assert.Equal(original.CreatedAt, stillThere.CreatedAt);
        }
    }
}
