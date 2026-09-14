using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0038, as amended: one workspace can hold many projects, each
/// connected at its own address. What is under test is the factory keeping
/// them apart — a server that says it serves another project is refused —
/// and the one kind of server that is deliberately shared: standards.
/// </summary>
[Collection("SpecGraph")]
public sealed class ProjectScopingTests(SpecGraphTestFixture fixture)
{
    private static FakeServerProbe CorpusServer(string? project)
    {
        var probe = new FakeServerProbe();
        probe.Answers("df.describe", new
        {
            name = "moonbeam-specs",
            convention_version = "0.1.0",
            domain = "corpus",
            capabilities = new[] { "df.describe" },
            requires = Array.Empty<string>(),
            effective_config = project is null
                ? (object)new { corpus = "moonbeam-specs" }
                : new { corpus = "moonbeam-specs", project },
        });
        return probe;
    }

    private async Task<(string Id, string Name, string OrgId)> ProjectAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId);
        return (project.Id, project.Name, orgId);
    }

    private static string NewUrl() => $"http://specs-{Guid.NewGuid():n}.invalid/projects/x/mcp";

    [Fact]
    public async Task AServerThatSaysItServesAnotherProjectIsRefusedAndNothingIsStored()
    {
        var (projectId, _, orgId) = await ProjectAsync();
        var url = NewUrl();

        await using var db = fixture.NewDb();
        var refusal = await Assert.ThrowsAsync<ServerRegistrationException>(() =>
            new ServerRegistry(db, CorpusServer("smashhit")).RegisterAsync(orgId, url, projectId: projectId));

        Assert.Contains("smashhit", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Servers.CountAsync(s => s.Url == url));
    }

    [Fact]
    public async Task AServerThatNamesThisProjectConnectsToIt()
    {
        var (projectId, name, orgId) = await ProjectAsync();

        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, CorpusServer(name.ToUpperInvariant()))
            .RegisterAsync(orgId, NewUrl(), projectId: projectId);

        Assert.Equal(projectId, registration.Server.ProjectId);
    }

    [Fact]
    public async Task AServerThatNamesNoProjectIsTakenAtItsWord()
    {
        var (projectId, _, orgId) = await ProjectAsync();

        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, CorpusServer(project: null))
            .RegisterAsync(orgId, NewUrl(), projectId: projectId);

        Assert.Equal(projectId, registration.Server.ProjectId);
    }

    [Fact]
    public async Task AStandardsServerBelongsToTheOrgWhicheverProjectConnectedIt()
    {
        var (first, _, orgId) = await ProjectAsync();
        var (second, _, _) = await ProjectAsync();
        var url = $"http://standards-{Guid.NewGuid():n}.invalid/mcp";
        var probe = new FakeServerProbe();
        probe.Answers("df.describe", FakeServerProbe.Describe(
            "moonbeam-standards", ["df.describe"], "0.1.0", domain: "standards"));

        // Each registration in its own context and each result read back
        // fresh: in one context both calls return the same tracked entity,
        // and the second would overwrite what the first is asserted on.
        string serverId;
        await using (var db = fixture.NewDb())
        {
            serverId = (await new ServerRegistry(db, probe).RegisterAsync(orgId, url, projectId: first)).Server.Id;
        }
        await using (var db = fixture.NewDb())
        {
            Assert.Null((await db.Servers.AsNoTracking().SingleAsync(s => s.Id == serverId)).ProjectId);
        }

        await using (var db = fixture.NewDb())
        {
            Assert.Equal(serverId,
                (await new ServerRegistry(db, probe).RegisterAsync(orgId, url, projectId: second)).Server.Id);
        }
        await using (var db = fixture.NewDb())
        {
            Assert.Null((await db.Servers.AsNoTracking().SingleAsync(s => s.Id == serverId)).ProjectId);
        }
    }
}
