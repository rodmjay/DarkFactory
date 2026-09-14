using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// What the architect is told about the world outside the graph. The
/// failure this exists for happened: a user asked "can you see the specs
/// from the MCP server?" and the architect answered that the project was
/// empty and suggested they check on their side — while the corpus server
/// had been answering every thirty seconds for three hours and 43 of its
/// documents sat in an intake. The model was right about what it was shown.
/// </summary>
[Collection("SpecGraph")]
public sealed class ConversationContextTests(SpecGraphTestFixture fixture)
{
    private static ConversationService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    /// <summary>
    /// A project in an org of its own. Standards servers are org-wide
    /// (ADR-0038), so in the fixture's shared org every other test's
    /// standards server would be one of this project's connections — which
    /// is correct behaviour and the wrong thing to be asserting about here.
    /// </summary>
    private async Task<(Project Project, string ConversationId)> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("n");
        var project = new Project
        {
            Id = $"proj_{suffix}",
            OrgId = $"org_ctx_{suffix}",
            Name = $"ctx-{suffix}",
            WorkspaceMcpUrl = $"http://example.invalid/{suffix}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var db = fixture.NewDb();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        await new TeamService(db).SeedDefaultTeamAsync(project.Id, project.OrgId);
        var conversation = await Service(new FakeModelGateway(), db).StartAsync(project.Id, "specs?", "tester");
        return (project, conversation.Id);
    }

    private static Server NewServer(
        string orgId, string name, string domain, string url, ServerStatus status, string? projectId = null) => new()
    {
        Id = Ulid.NewUlid(),
        OrgId = orgId,
        ProjectId = projectId,
        Url = url,
        Name = name,
        Tier = ServerTier.Community,
        Domain = domain,
        ConventionVersion = "0.1.0",
        ManifestJson = "{}",
        Status = status,
        RegisteredAt = DateTimeOffset.UtcNow.AddDays(-1),
        LastSeenAt = status == ServerStatus.Unreachable ? DateTimeOffset.UtcNow.AddMinutes(-10) : DateTimeOffset.UtcNow,
        UnreachableSince = status == ServerStatus.Unreachable ? DateTimeOffset.UtcNow.AddMinutes(-9) : null,
        LastError = status == ServerStatus.Unreachable ? "connection refused" : null,
    };

    private async Task<(FakeModelGateway Gateway, ContextPack Pack)> TurnAsync(string conversationId)
    {
        var gateway = new FakeModelGateway().RespondsChatting("They are.");
        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "can you see the specs from the MCP server?", "tester");
        }

        await using var verify = fixture.NewDb();
        var artifact = await new PostgresArtifactStore(verify).GetAsync(result.ContextRef);
        return (gateway, JsonSerializer.Deserialize<ContextPack>(artifact!.ContentJson)!);
    }

    [Fact]
    public async Task TheArchitectIsToldWhatTheProjectIsConnectedToAndHowEachStands()
    {
        var (project, conversationId) = await SeedAsync();
        await using (var db = fixture.NewDb())
        {
            db.Servers.AddRange(
                NewServer(project.OrgId, "moonbeam-specs", "corpus", $"http://specs-{project.Id}/mcp", ServerStatus.Conformant, project.Id),
                // Bound by URL, as a project's workspace is — it has no project_id.
                NewServer(project.OrgId, "drones-workspace", "workspace", project.WorkspaceMcpUrl, ServerStatus.Unreachable),
                NewServer(project.OrgId, "someone-elses", "corpus", $"http://other-{project.Id}/mcp", ServerStatus.Conformant, "proj_other"));
            await db.SaveChangesAsync();
        }

        var (gateway, pack) = await TurnAsync(conversationId);

        Assert.Equal(["drones-workspace", "moonbeam-specs"], pack.Connections.Select(c => c.Name).Order());
        var prompt = gateway.Requests[0].SystemPrompt;
        Assert.Contains("`moonbeam-specs` (corpus) — Conformant, last answered", prompt, StringComparison.Ordinal);
        Assert.Contains("`drones-workspace` (workspace) — Unreachable since", prompt, StringComparison.Ordinal);
        Assert.Contains("connection refused", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("someone-elses", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportedDocumentsAreNamedAsExistingSpecificationsNotAsAnEmptyProject()
    {
        var (project, conversationId) = await SeedAsync();
        await using (var db = fixture.NewDb())
        {
            await new IntakeService(db, new FakeModelGateway(), new PostgresArtifactStore(db), new TeamService(db),
                    new SpecGraphService(db), new SpecDiffTranslator(db))
                .StartAsync(project.Id, "drones", [
                    new("moonbeam-specs:drones/0000-drones-northstar", "Drones — Northstar",
                        "---\nstatus: draft\n---\n# Drones — Northstar\n\n> One world, shared by everybody.\n"),
                    new("moonbeam-specs:drones/0079-the-working-swarm", "The Working Swarm",
                        "# The Working Swarm\n\n> The drones you are not flying run the whole cycle themselves.\n"),
                ], "tester");
        }

        var (gateway, pack) = await TurnAsync(conversationId);

        var import = Assert.Single(pack.Imports);
        Assert.Equal(2, import.Documents.Count);
        Assert.Equal("One world, shared by everybody.", import.Documents[0].Summary);

        var prompt = gateway.Requests[0].SystemPrompt;
        Assert.Contains("## Imported, not yet in the graph", prompt, StringComparison.Ordinal);
        Assert.Contains("`moonbeam-specs:drones/0079-the-working-swarm` — The Working Swarm (pending): The drones you are not flying",
            prompt, StringComparison.Ordinal);
        Assert.Contains("2 imported document(s) are awaiting extraction", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("this project has no specifications", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectWithNothingConnectedAndNothingImportedIsStillCalledEmpty()
    {
        var (_, conversationId) = await SeedAsync();

        var (gateway, pack) = await TurnAsync(conversationId);

        Assert.Empty(pack.Connections);
        Assert.Empty(pack.Imports);
        var prompt = gateway.Requests[0].SystemPrompt;
        Assert.Contains("not connected to any server", prompt, StringComparison.Ordinal);
        Assert.Contains("this project has no specifications", prompt, StringComparison.Ordinal);
    }
}
