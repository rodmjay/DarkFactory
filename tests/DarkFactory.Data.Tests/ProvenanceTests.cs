using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0016: "Every amendment carries provenance." Required means the
/// database rejects a revision, edge or snapshot without it — not that the
/// service remembers to set it.
/// </summary>
[Collection("SpecGraph")]
public sealed class ProvenanceTests(SpecGraphTestFixture fixture)
{
    // Raw string literals treat '{' as interpolation, so the JSON goes
    // through a constant rather than being escaped inline.
    private const string EmptyJson = "{}";

    private const string NotNullViolation = "23502";
    private const string ForeignKeyViolation = "23503";

    private static async Task<PostgresException> AssertRejectedAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task EveryApprovalStampsOneProvenanceRowOnEverythingItTouches()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(creates:
        [
            Graph.Create("A billing period is one calendar month."),
            Graph.Create("Proration is computed daily."),
        ]));

        await using var db = fixture.NewDb();
        var specIds = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).ToListAsync();
        var revisions = await db.SpecRevisions.AsNoTracking().Where(r => specIds.Contains(r.SpecId)).ToListAsync();

        Assert.Equal(2, revisions.Count);
        Assert.All(revisions, r => Assert.False(string.IsNullOrEmpty(r.ProvenanceId)));

        // One approval, one provenance row: the two revisions share it, so
        // "which decision produced this" has a single answer rather than
        // one indistinguishable copy per row.
        var provenanceId = Assert.Single(revisions.Select(r => r.ProvenanceId).Distinct());

        var provenance = await db.Provenance.AsNoTracking().SingleAsync(p => p.Id == provenanceId);
        Assert.Equal(conversationId, provenance.ConversationId);
        Assert.Equal(ActorType.Human, provenance.ActorType);
        Assert.Equal("tester", provenance.ApprovedBy);
    }

    [Fact]
    public async Task ARevisionWithoutProvenanceIsRejected()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create("Refunds require a reason code.")]));

        string specId;
        await using (var db = fixture.NewDb())
        {
            specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
        }

        // Attempted over the *owner* connection deliberately: even the role
        // that owns the table cannot write an unprovenanced revision,
        // because this is a NOT NULL and a foreign key, not a grant.
        var nullProvenance = await AssertRejectedAsync(fixture.OwnerConnectionString,
            $"""
             INSERT INTO spec_revisions (spec_id, hash, content_json, canonical_text, created_at, provenance_id)
             VALUES ('{specId}', '{new string('a', 64)}', '{EmptyJson}', 'x', now(), NULL)
             """);
        Assert.Equal(NotNullViolation, nullProvenance.SqlState);

        var danglingProvenance = await AssertRejectedAsync(fixture.OwnerConnectionString,
            $"""
             INSERT INTO spec_revisions (spec_id, hash, content_json, canonical_text, created_at, provenance_id)
             VALUES ('{specId}', '{new string('b', 64)}', '{EmptyJson}', 'x', now(), 'no_such_provenance')
             """);
        Assert.Equal(ForeignKeyViolation, danglingProvenance.SqlState);
    }

    [Fact]
    public async Task AnEdgeWithoutProvenanceIsRejected()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId, Graph.Diff(creates:
        [
            Graph.Create("Seats are billed monthly."),
            Graph.Create("Seat count cannot decrease mid-term."),
        ]));

        List<string> specIds;
        await using (var db = fixture.NewDb())
        {
            specIds = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).ToListAsync();
        }

        var rejected = await AssertRejectedAsync(fixture.OwnerConnectionString,
            $"""
             INSERT INTO spec_edges (id, project_id, org_id, from_spec_id, to_spec_id, kind, created_at, provenance_id)
             VALUES ('edge_orphan', '{projectId}', '{orgId}', '{specIds[0]}', '{specIds[1]}', 'depends_on', now(), NULL)
             """);
        Assert.Equal(NotNullViolation, rejected.SqlState);
    }

    [Fact]
    public async Task ProvenanceRowsCarryTheConversationTheyCameFrom()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        string turnId;
        await using (var db = fixture.NewDb())
        {
            var turn = new Turn
            {
                Id = Ulid.NewUlid(),
                ConversationId = conversationId,
                Seq = 1,
                Role = TurnRole.User,
                Content = "Cancellations should be effective at period end.",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Turns.Add(turn);
            await db.SaveChangesAsync();
            turnId = turn.Id;
        }

        Amendment amendment;
        await using (var db = fixture.NewDb())
        {
            amendment = await new SpecGraphService(db).ProposeAsync(
                projectId, orgId, conversationId, turnId, proposedBy: "architect",
                Graph.Diff(creates: [Graph.Create("Cancellation takes effect at period end.")]));
        }

        await using (var db = fixture.NewDb())
        {
            await new SpecGraphService(db).ApproveAsync(amendment.Id, approvedBy: "rod");
        }

        await using (var db = fixture.NewDb())
        {
            var specId = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
            var revision = await db.SpecRevisions.AsNoTracking().SingleAsync(r => r.SpecId == specId);
            var provenance = await db.Provenance.AsNoTracking().SingleAsync(p => p.Id == revision.ProvenanceId);

            // The whole chain: revision → provenance → turn → conversation.
            Assert.Equal(conversationId, provenance.ConversationId);
            Assert.Equal(turnId, provenance.TurnId);
            Assert.Equal("rod", provenance.ApprovedBy);
        }
    }
}
