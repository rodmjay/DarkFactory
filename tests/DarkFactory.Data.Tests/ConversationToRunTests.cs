using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// 3c's whole point, in one path: a conversation settles into a proposal, a
/// human approves it, and that approval — not a prompt someone typed — is
/// what seeds a run.
/// </summary>
[Collection("SpecGraph")]
public sealed class ConversationToRunTests(SpecGraphTestFixture fixture)
{
    [Fact]
    public async Task ApprovedAmendmentsSeedARunAtPlanAgainstAFreshSnapshot()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        // 1. Converse until it settles into a proposal.
        var gateway = new FakeModelGateway().RespondsProposing(new
        {
            creates = new[]
            {
                new { kind = "rule", layer = "domain", text = "Renewal is blocked if the account is delinquent." },
                new { kind = "rule", layer = "domain", text = "An account is delinquent after 30 days unpaid." },
            },
            revises = Array.Empty<object>(),
            retires = Array.Empty<object>(),
            // A node created in this same diff, referenced before it has an
            // id — the factory assigns ids, so the model says "new:N".
            edge_adds = new[] { new { from_spec_id = "new:0", to_spec_id = "new:1", kind = "depends_on" } },
            edge_retires = Array.Empty<object>(),
        });

        string conversationId, amendmentId;
        await using (var db = fixture.NewDb())
        {
            var conversations = new ConversationService(
                db, gateway, new PostgresArtifactStore(db), new TeamService(db),
                new SpecGraphService(db), new SpecDiffTranslator(db));

            var conversation = await conversations.StartAsync(projectId, "renewals", "rod");
            conversationId = conversation.Id;

            var turn = await conversations.TurnAsync(
                conversationId, "Block renewals for delinquent accounts.", "rod");
            amendmentId = turn.AmendmentId!;
        }

        Assert.NotNull(amendmentId);

        // 2. Approve. Nothing has touched the graph until this point.
        await using (var db = fixture.NewDb())
        {
            Assert.Empty(await db.SpecNodes.AsNoTracking().Where(n => n.ProjectId == projectId).ToListAsync());
            await new SpecGraphService(db).ApproveAsync(amendmentId, "rod");
        }

        string firstSpecId, secondSpecId;
        await using (var db = fixture.NewDb())
        {
            var nodes = await db.SpecNodes.AsNoTracking()
                .Where(n => n.ProjectId == projectId).OrderBy(n => n.SpecId).ToListAsync();
            Assert.Equal(2, nodes.Count);

            // The forward reference resolved to real ids, and the edge
            // points at the two nodes this amendment created.
            var edge = await db.SpecEdges.AsNoTracking().SingleAsync(e => e.ProjectId == projectId);
            Assert.Equal("depends_on", edge.Kind);
            Assert.Contains(edge.FromSpecId, nodes.Select(n => n.SpecId));
            Assert.Contains(edge.ToSpecId, nodes.Select(n => n.SpecId));
            Assert.NotEqual(edge.FromSpecId, edge.ToSpecId);

            firstSpecId = nodes[0].SpecId;
            secondSpecId = nodes[1].SpecId;
        }

        // 3. Seed the run.
        Run run;
        await using (var db = fixture.NewDb())
        {
            run = await new WorkService(db, new SpecGraphService(db))
                .CreateAsync(projectId, [amendmentId], "rod");
        }

        Assert.Equal(StageId.Plan, run.CurrentStage);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.NotNull(run.SnapshotId);
        Assert.Equal([amendmentId], run.AmendmentIds!);

        // The snapshot the run records contains the approved specs — which
        // is what makes "what was this built against?" answerable later.
        await using (var verify = fixture.NewDb())
        {
            var members = await verify.SnapshotMembers.AsNoTracking()
                .Where(m => m.SnapshotId == run.SnapshotId).ToListAsync();

            Assert.Equal(2, members.Count);
            Assert.Contains(members, m => m.SpecId == firstSpecId);
            Assert.Contains(members, m => m.SpecId == secondSpecId);

            var edges = await verify.SnapshotEdges.AsNoTracking()
                .Where(e => e.SnapshotId == run.SnapshotId).ToListAsync();
            Assert.Single(edges);
        }
    }

    [Fact]
    public async Task AnUnapprovedAmendmentCannotSeedARun()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        var gateway = new FakeModelGateway()
            .RespondsProposing(FakeModelGateway.CreateDiff("Something worth approving."));

        string amendmentId;
        await using (var db = fixture.NewDb())
        {
            var conversations = new ConversationService(
                db, gateway, new PostgresArtifactStore(db), new TeamService(db),
                new SpecGraphService(db), new SpecDiffTranslator(db));

            var conversation = await conversations.StartAsync(projectId, null, "rod");
            amendmentId = (await conversations.TurnAsync(conversation.Id, "go", "rod")).AmendmentId!;
        }

        // docs/adr/0017's gate, enforced where a proposal becomes something
        // that changes code — not merely documented.
        await using var check = fixture.NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WorkService(check, new SpecGraphService(check)).CreateAsync(projectId, [amendmentId], "rod"));

        Assert.Contains("Only approved amendments", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Proposed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARejectedAmendmentNeverReachesTheGraph()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        var gateway = new FakeModelGateway().RespondsProposing(FakeModelGateway.CreateDiff("Not this."));

        string amendmentId;
        await using (var db = fixture.NewDb())
        {
            var conversations = new ConversationService(
                db, gateway, new PostgresArtifactStore(db), new TeamService(db),
                new SpecGraphService(db), new SpecDiffTranslator(db));
            var conversation = await conversations.StartAsync(projectId, null, "rod");
            amendmentId = (await conversations.TurnAsync(conversation.Id, "go", "rod")).AmendmentId!;
        }

        await using (var db = fixture.NewDb())
        {
            await new SpecGraphService(db).RejectAsync(amendmentId, "rod", "not what I meant");
        }

        await using var verify = fixture.NewDb();
        Assert.Empty(await verify.SpecNodes.AsNoTracking().Where(n => n.ProjectId == projectId).ToListAsync());
        Assert.Equal(AmendmentStatus.Rejected,
            (await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == amendmentId)).Status);
    }

    [Fact]
    public async Task CreatingARunWithNoAmendmentsIsRefused()
    {
        var (projectId, _, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WorkService(db, new SpecGraphService(db)).CreateAsync(projectId, [], "rod"));

        // work.submit(input: string) is gone; this is what replaced it.
        Assert.Contains("created from approved amendments", ex.Message, StringComparison.Ordinal);
    }
}
