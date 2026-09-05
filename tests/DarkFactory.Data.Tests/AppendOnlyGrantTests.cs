using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The append-only guarantee in docs/adr/0016 is a database grant, not a
/// code convention. These tests connect as the restricted role that
/// `factory` and `worker` actually use — a test over the owner connection
/// would pass regardless of what the migration says, because a Postgres
/// table owner bypasses its own REVOKEs.
/// </summary>
[Collection("SpecGraph")]
public sealed class AppendOnlyGrantTests(SpecGraphTestFixture fixture)
{
    private const string InsufficientPrivilege = "42501";

    private async Task<(string SpecId, string Hash, string ProvenanceId)> SeedOneRevisionAsync()
    {
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();
        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create($"Grant probe {Guid.NewGuid():n}.")]));

        await using var db = fixture.NewDb();
        var node = await db.SpecNodes.AsNoTracking().SingleAsync(n => n.ProjectId == projectId);
        var revision = await db.SpecRevisions.AsNoTracking().SingleAsync(r => r.SpecId == node.SpecId);
        return (revision.SpecId, revision.Hash, revision.ProvenanceId);
    }

    private static async Task<PostgresException> AssertRefusedAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<int> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ApplicationRoleCannotUpdateRevisionRows()
    {
        var (specId, hash, _) = await SeedOneRevisionAsync();
        var target = $"WHERE spec_id = '{specId}' AND hash = '{hash}'";

        var update = await AssertRefusedAsync(fixture.AppConnectionString,
            $"UPDATE spec_revisions SET canonical_text = 'tampered' {target}");
        Assert.Equal(InsufficientPrivilege, update.SqlState);

        var delete = await AssertRefusedAsync(fixture.AppConnectionString,
            $"DELETE FROM spec_revisions {target}");
        Assert.Equal(InsufficientPrivilege, delete.SqlState);

        // The control half. Without it, the two assertions above would pass
        // just as happily against a role that cannot see the table at all,
        // or against a WHERE clause that matches nothing.
        Assert.Equal(1, await ExecuteAsync(fixture.OwnerConnectionString,
            $"UPDATE spec_revisions SET canonical_text = canonical_text {target}"));

        // ...and the revoke is targeted, not a blanket read-only role:
        // appending is exactly what the application must still be able to do.
        await using var appDb = fixture.NewAppDb();
        Assert.NotNull(await appDb.SpecRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.SpecId == specId && r.Hash == hash));
    }

    [Fact]
    public async Task ApplicationRoleCannotMutateProvenance()
    {
        var (_, _, provenanceId) = await SeedOneRevisionAsync();

        var update = await AssertRefusedAsync(fixture.AppConnectionString,
            $"UPDATE provenance SET actor_id = 'someone else' WHERE id = '{provenanceId}'");
        Assert.Equal(InsufficientPrivilege, update.SqlState);

        var delete = await AssertRefusedAsync(fixture.AppConnectionString,
            $"DELETE FROM provenance WHERE id = '{provenanceId}'");
        Assert.Equal(InsufficientPrivilege, delete.SqlState);

        Assert.Equal(1, await ExecuteAsync(fixture.OwnerConnectionString,
            $"UPDATE provenance SET actor_id = actor_id WHERE id = '{provenanceId}'"));
    }

    [Fact]
    public async Task ApplicationRoleCanStillAppendAndRetire()
    {
        // The negative tests above are only meaningful if the role can do
        // its actual job. Retiring a node is an UPDATE, and it must work:
        // spec_nodes is append-only in content, not in lifecycle.
        var (projectId, orgId, conversationId) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewAppDb();
        var service = new SpecGraphService(db);

        var amendment = await service.ProposeAsync(
            projectId, orgId, conversationId, turnId: null, proposedBy: "app-role", Graph.Diff(
                creates: [Graph.Create("Written by the restricted role.")]));
        await service.ApproveAsync(amendment.Id, approvedBy: "app-role");

        await using var retireDb = fixture.NewAppDb();
        var retireService = new SpecGraphService(retireDb);
        var specId = await retireDb.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();

        var retirement = await retireService.ProposeAsync(
            projectId, orgId, conversationId, turnId: null, proposedBy: "app-role", Graph.Diff(
                retires: [new SpecDiffNodeRetire(specId)]));
        await retireService.ApproveAsync(retirement.Id, approvedBy: "app-role");

        await using var verify = fixture.NewDb();
        Assert.NotNull((await verify.SpecNodes.AsNoTracking().SingleAsync(n => n.SpecId == specId)).RetiredAt);
    }
}
