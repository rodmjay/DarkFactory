using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0023: the factory keeps its own copy of a standards server's
/// documents, refreshes it without being asked, and hands an extraction the
/// standards its document names — so "how we build" arrives beside "what we
/// build" instead of being guessed at.
/// </summary>
[Collection("SpecGraph")]
public sealed class StandardsIngestTests(SpecGraphTestFixture fixture)
{
    private sealed record Standard(string Id, string Title, string Text, string Updated = "2026-09-07");

    private static List<Standard> Corpus() =>
    [
        new("web-game-structure", "Web game structure", "Rule 11: a system that stops says why."),
        new("performance-budget", "Performance budget", "Sixty frames on the reference laptop."),
    ];

    private static FakeServerProbe StandardsServer(List<Standard> standards)
    {
        var probe = new FakeServerProbe();
        probe.Answers("df.describe", FakeServerProbe.Describe(
            "moonbeam-standards", ["df.describe", "df.standards.list", "df.standards.get"], "0.1.0", domain: "standards"));
        probe.Handlers["df.standards.list"] = _ => ProbeResult.Success(JsonSerializer.Serialize(new
        {
            standards = standards.Select(s => new { id = s.Id, title = s.Title, layers = new[] { "all" }, status = "accepted", updated = s.Updated }),
        }), 1);
        probe.Handlers["df.standards.get"] = args =>
        {
            var s = standards.Single(x => x.Id == (string)args["id"]!);
            return ProbeResult.Success(JsonSerializer.Serialize(new { id = s.Id, title = s.Title, text = s.Text, updated = s.Updated }), 1);
        };
        return probe;
    }

    private async Task<(string ProjectId, string OrgId, string ServerId)> RegisterAsync(FakeServerProbe probe)
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, probe)
            .RegisterAsync(orgId, $"http://standards-{Guid.NewGuid():n}.invalid/mcp", projectId: projectId);
        return (projectId, orgId, registration.Server.Id);
    }

    private async Task<StandardsIngest> IngestAsync(FakeServerProbe probe, string serverId)
    {
        await using var db = fixture.NewDb();
        return await new StandardsIngestService(db, probe).IngestAsync(serverId);
    }

    [Fact]
    public async Task IngestKeepsEveryListedStandardWithItsText()
    {
        var probe = StandardsServer(Corpus());
        var (projectId, _, serverId) = await RegisterAsync(probe);

        var ingest = await IngestAsync(probe, serverId);

        Assert.Equal(2, ingest.Ingested);
        await using var db = fixture.NewDb();
        var row = await db.StandardsIndex.SingleAsync(s => s.ServerId == serverId && s.ChunkRef == "web-game-structure");
        Assert.Equal("Rule 11: a system that stops says why.", row.Text);
        Assert.Equal("moonbeam-standards:web-game-structure", row.SourceRef);
        Assert.Equal("2026-09-07", row.Updated);
        Assert.Equal(projectId, row.ProjectId);
    }

    [Fact]
    public async Task ReingestReplacesSoAStandardDeletedAtTheSourceStopsBeingServed()
    {
        var standards = Corpus();
        var probe = StandardsServer(standards);
        var (_, _, serverId) = await RegisterAsync(probe);
        await IngestAsync(probe, serverId);

        standards.RemoveAt(1);
        standards[0] = standards[0] with { Text = "Rule 11, revised.", Updated = "2026-09-14" };
        var again = await IngestAsync(probe, serverId);

        Assert.Equal(1, again.Ingested);
        Assert.Equal(1, again.Removed);
        await using var db = fixture.NewDb();
        var row = await db.StandardsIndex.SingleAsync(s => s.ServerId == serverId);
        Assert.Equal("Rule 11, revised.", row.Text);
    }

    [Fact]
    public async Task AHealthCheckIngestsAStandardsServerNobodyHasIngestedYet()
    {
        var probe = StandardsServer(Corpus());
        var (_, _, serverId) = await RegisterAsync(probe);

        await using (var db = fixture.NewDb())
        {
            var server = await db.Servers.SingleAsync(s => s.Id == serverId);
            await new ServerHealthService(db, probe, new ServerRegistry(db, probe), new StandardsIngestService(db, probe))
                .CheckAsync(server, DateTimeOffset.UtcNow.AddSeconds(31));
        }

        await using var verify = fixture.NewDb();
        Assert.Equal(2, await verify.StandardsIndex.CountAsync(s => s.ServerId == serverId));
    }

    [Fact]
    public async Task AnExtractionIsShownTheStandardsItsDocumentNamesAndToldWhichAreMissing()
    {
        var probe = StandardsServer(Corpus());
        var (projectId, orgId, serverId) = await RegisterAsync(probe);
        await IngestAsync(probe, serverId);

        var gateway = new FakeModelGateway().Responds(JsonSerializer.Serialize(new
        {
            reply = "Extracted.",
            draft = new
            {
                creates = new[] { new { kind = "rule", layer = "swarm", text = "An idle drone says why it is idle.", rationale = "Commanding." } },
                revises = Array.Empty<object>(), retires = Array.Empty<object>(),
                edge_adds = Array.Empty<object>(), edge_retires = Array.Empty<object>(),
            },
            holes = Array.Empty<object>(),
        }));

        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        var intake = new IntakeService(db, gateway, new PostgresArtifactStore(db), new TeamService(db),
            new SpecGraphService(db), new SpecDiffTranslator(db));
        var started = await intake.StartAsync(projectId, "drones", [
            new("moonbeam-specs:drones/0079-the-working-swarm", "The Working Swarm",
                "---\nstatus: draft\nstandards: [web-game-structure, modular-parts]\n---\n# The Working Swarm\n> A swarm that stops says why.\n"),
        ], "tester");

        await intake.ExtractAsync(started.Sources[0].Id, "tester");

        var prompt = gateway.Requests[0].SystemPrompt;
        Assert.Contains("## Standards this document names", prompt, StringComparison.Ordinal);
        Assert.Contains("Rule 11: a system that stops says why.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Sixty frames", prompt, StringComparison.Ordinal); // not named by this document
        Assert.Contains("- `modular-parts`", prompt, StringComparison.Ordinal);  // named, not served
    }

    [Fact]
    public void TheStandardsADocumentNamesAreReadFromItsFrontMatterOnly()
    {
        Assert.Equal(["a", "b-c"], StandardsIngestService.NamedIn("---\nstatus: draft\nstandards: [a, b-c]  \n---\n# T\n"));
        Assert.Empty(StandardsIngestService.NamedIn("# T\n\nstandards: [a]\n"));
        Assert.Empty(StandardsIngestService.NamedIn("---\nstatus: draft\n---\n"));
    }
}
