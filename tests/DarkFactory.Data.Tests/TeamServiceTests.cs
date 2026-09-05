using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DarkFactory.Data.Tests;

/// <summary>docs/adr/0028: a project has one standing team, and it decides who handles what.</summary>
[Collection("SpecGraph")]
public sealed class TeamServiceTests(SpecGraphTestFixture fixture)
{
    [Fact]
    public async Task SeedingGivesEveryAssignmentPointSomebody()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        var team = await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);

        Assert.True(team.IsActive);

        var service = new TeamService(fixture.NewDb());
        foreach (var template in TeamService.DefaultTemplate)
        {
            var resolved = await service.ResolveAsync(projectId, template.Point);
            Assert.Equal(template.Role, resolved.Role);
            // docs/adr/0027 names deployments after roles, so the default
            // mapping is the identity until someone changes it.
            Assert.Equal(template.Role, resolved.Deployment);
        }
    }

    [Fact]
    public async Task SeedingIsIdempotent()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        string first;
        await using (var db = fixture.NewDb())
        {
            first = (await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId)).Id;
        }
        await using (var db = fixture.NewDb())
        {
            Assert.Equal(first, (await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId)).Id);
        }

        await using var verify = fixture.NewDb();
        Assert.Single(await verify.Teams.AsNoTracking().Where(t => t.ProjectId == projectId).ToListAsync());
    }

    [Fact]
    public async Task RetargetingARoleToADifferentDeploymentIsJustAnUpdate()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        await using (var db = fixture.NewDb())
        {
            var member = await db.TeamMembers.SingleAsync(m =>
                m.Role == AgentRoles.Planner && db.Teams.Any(t => t.Id == m.TeamId && t.ProjectId == projectId));
            member.Deployment = "planner-preview";
            member.FallbackDeployment = "planner";
            await db.SaveChangesAsync();
        }

        // docs/adr/0022: which model does what is configuration, and
        // changing it must not require a migration or a deploy.
        var resolved = await new TeamService(fixture.NewDb()).ResolveAsync(projectId, AssignmentPoints.Plan);
        Assert.Equal("planner-preview", resolved.Deployment);
        Assert.Equal("planner", resolved.FallbackDeployment);
    }

    [Fact]
    public async Task AProjectCannotHaveTwoActiveTeams()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        // "The project's active team" has to have exactly one answer, and
        // the database is what guarantees it rather than a convention
        // someone could forget (docs/adr/0028).
        await using var db2 = fixture.NewDb();
        db2.Teams.Add(new Team
        {
            Id = Ulid.NewUlid(),
            ProjectId = projectId,
            OrgId = orgId,
            Name = "second",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
    }

    [Fact]
    public async Task ResolvingAnUnassignedPointSaysWhichOne()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        var ex = await Assert.ThrowsAsync<TeamNotConfiguredException>(
            () => new TeamService(fixture.NewDb()).ResolveAsync(projectId, AssignmentPoints.Ship));

        Assert.Contains(AssignmentPoints.Ship, ex.Message, StringComparison.Ordinal);
    }
}
