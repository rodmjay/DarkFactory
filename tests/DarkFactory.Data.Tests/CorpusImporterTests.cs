using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0038 and docs/conventions/corpus.md: specifications extracted
/// from an MCP server rather than pasted in. The promise under test is the
/// hash — every document imported is the one the server listed, which is
/// what makes drift checkable later — and the refusals around it.
/// </summary>
[Collection("SpecGraph")]
public sealed class CorpusImporterTests(SpecGraphTestFixture fixture)
{
    private sealed record Doc(string Id, string Status, string Text);

    private const string Northstar = "drones/0000-drones-northstar";
    private const string Swarm = "drones/0079-the-working-swarm";
    private const string Land = "drones/0084-choosing-your-first-land";

    private static List<Doc> Corpus() =>
    [
        new(Northstar, "draft", "---\nstatus: draft\n---\n# Northstar\n> One world, shared by everybody.\n"),
        new(Swarm, "draft", "---\nstatus: draft\n---\n# The Working Swarm\n> The drones you are not flying work.\n"),
        new(Land, "superseded", "---\nstatus: superseded\n---\n# Choosing Your First Land\n> Replaced.\n"),
    ];

    /// <summary>A corpus server over a mutable list, so a test can change the source after a pull.</summary>
    private static FakeServerProbe CorpusServer(List<Doc> docs, Func<Doc, string>? served = null)
    {
        var probe = new FakeServerProbe();
        probe.Answers("df.describe", FakeServerProbe.Describe(
            "moonbeam-specs", ["df.describe", "df.corpus.list", "df.corpus.get"], "0.1.0", domain: "corpus"));

        probe.Handlers["df.corpus.list"] = _ => ProbeResult.Success(JsonSerializer.Serialize(new
        {
            documents = docs.Select(d => new
            {
                id = d.Id,
                title = d.Id,
                area = d.Id.Split('/')[0],
                status = d.Status,
                updated = "2026-09-07",
                sha256 = SpecGraphService.ComputeHash(d.Text),
                superseded_by = (string?)null,
            }),
        }), 1);

        probe.Handlers["df.corpus.get"] = args =>
        {
            var id = (string)args["id"]!;
            var doc = docs.Single(d => d.Id == id);
            var text = served?.Invoke(doc) ?? doc.Text;
            return ProbeResult.Success(JsonSerializer.Serialize(new
            {
                id,
                title = id,
                area = doc.Id.Split('/')[0],
                status = doc.Status,
                updated = "2026-09-07",
                sha256 = SpecGraphService.ComputeHash(text),
                text,
            }), 1);
        };

        return probe;
    }

    private async Task<(string ProjectId, string ServerId, ServerStatus Status)> RegisterAsync(FakeServerProbe probe)
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        var registration = await new ServerRegistry(db, probe)
            .RegisterAsync(orgId, $"http://specs-{Guid.NewGuid():n}.invalid/mcp", projectId: projectId);
        return (projectId, registration.Server.Id, registration.Server.Status);
    }

    private static CorpusImporter Importer(FakeServerProbe probe, DarkFactoryDbContext db) =>
        new(db, probe, new IntakeService(db, new FakeModelGateway(), new PostgresArtifactStore(db),
            new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db)));

    [Fact]
    public async Task ACorpusServerIsConformantWhenADocumentHashesToWhatItListed()
    {
        var probe = CorpusServer(Corpus());
        var (_, serverId, status) = await RegisterAsync(probe);

        Assert.Equal(ServerStatus.Conformant, status);
        await using var db = fixture.NewDb();
        var results = await db.ConformanceResults.Where(c => c.ServerId == serverId).ToListAsync();
        Assert.Equal(ConformanceStatus.Passed, results.Single(r => r.Capability == "df.corpus.list").Status);
        Assert.Equal(ConformanceStatus.Passed, results.Single(r => r.Capability == "df.corpus.get").Status);
    }

    [Fact]
    public async Task ACorpusServerServingADifferentBodyThanItListedIsDegraded()
    {
        var probe = CorpusServer(Corpus(), served: d => d.Text + "\nedited after listing");
        var (_, serverId, status) = await RegisterAsync(probe);

        Assert.Equal(ServerStatus.Degraded, status);
        await using var db = fixture.NewDb();
        var get = await db.ConformanceResults.SingleAsync(c => c.ServerId == serverId && c.Capability == "df.corpus.get");
        Assert.Equal(ConformanceStatus.Failed, get.Status);
        Assert.Contains("hashes to", get.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APullImportsEveryLiveDocumentAndLeavesTheSupersededOneOut()
    {
        var docs = Corpus();
        var probe = CorpusServer(docs);
        var (projectId, serverId, _) = await RegisterAsync(probe);

        IntakeStarted started;
        await using (var db = fixture.NewDb())
        {
            started = await Importer(probe, db).PullAsync(projectId, serverId, null, "drones", false, "tester");
        }

        Assert.Equal(serverId, started.Intake.SourceServerId);
        // Named for the area it pulled, which is what makes a second pull of it recognisable.
        Assert.Equal("drones", started.Intake.Name);
        Assert.Equal(
            [$"moonbeam-specs:{Northstar}", $"moonbeam-specs:{Swarm}"],
            started.Sources.OrderBy(s => s.Seq).Select(s => s.SourceRef));

        var swarm = started.Sources.Single(s => s.OriginId == Swarm);
        Assert.Equal(docs[1].Text, swarm.Content);
        Assert.Equal(SpecGraphService.ComputeHash(docs[1].Text), swarm.OriginSha256);
        Assert.Equal("2026-09-07", swarm.OriginUpdated);
    }

    [Fact]
    public async Task APullRefusesABodyThatIsNotTheOneListedAndImportsNothing()
    {
        // Only the second document is tampered with, so registration's
        // conformance probe (which fetches the first) passes and the pull is
        // what has to catch it.
        var probe = CorpusServer(Corpus(), served: d => d.Id == Swarm ? d.Text + "tampered" : d.Text);
        var (projectId, serverId, status) = await RegisterAsync(probe);
        Assert.Equal(ServerStatus.Conformant, status);

        await using var db = fixture.NewDb();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Importer(probe, db).PullAsync(projectId, serverId, null, null, false, "tester"));

        Assert.Contains(Swarm, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("could not be checked for drift", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Intakes.CountAsync(i => i.ProjectId == projectId));
    }

    [Fact]
    public async Task APullFromAnUnreachableServerSaysSoAndImportsNothing()
    {
        var probe = CorpusServer(Corpus());
        var (projectId, serverId, _) = await RegisterAsync(probe);

        await using var db = fixture.NewDb();
        var server = await db.Servers.SingleAsync(s => s.Id == serverId);
        server.Status = ServerStatus.Unreachable;
        server.UnreachableSince = DateTimeOffset.UtcNow.AddMinutes(-3);
        server.LastError = "connection refused";
        await db.SaveChangesAsync();

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Importer(probe, db).PullAsync(projectId, serverId, null, null, false, "tester"));

        Assert.Contains("unreachable since", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("connection refused", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(probe.CallsTo("df.corpus.get").Skip(1)); // only registration's probe
        Assert.Equal(0, await db.Intakes.CountAsync(i => i.ProjectId == projectId));
    }

    [Fact]
    public async Task OnlyACorpusServerCanBePulledFrom()
    {
        var probe = FakeServerProbe.HealthyWorkspace();
        var (projectId, serverId, _) = await RegisterAsync(probe);

        await using var db = fixture.NewDb();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Importer(probe, db).PullAsync(projectId, serverId, null, null, false, "tester"));
        Assert.Contains("not a corpus server", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APreviewCountsWhatAPullWouldBringInAreaByArea()
    {
        var docs = Corpus();
        docs.Add(new Doc("smashhit/0001-glass-fracture", "draft", "# Glass fracture\n"));
        var probe = CorpusServer(docs);
        var (_, serverId, _) = await RegisterAsync(probe);

        await using var db = fixture.NewDb();
        var areas = await Importer(probe, db).PreviewAsync(serverId);

        Assert.Equal(["drones", "smashhit"], areas.Select(a => a.Area));
        Assert.Equal(2, areas[0].Documents); // northstar and swarm
        Assert.Equal(1, areas[0].Retired);   // the superseded one
        Assert.Equal(1, areas[1].Documents);
    }

    [Fact]
    public async Task TheSameAreaCannotBePulledTwiceAndIsListedOnce()
    {
        var probe = CorpusServer(Corpus());
        var (projectId, serverId, _) = await RegisterAsync(probe);

        await using var db = fixture.NewDb();
        var importer = Importer(probe, db);
        await importer.PullAsync(projectId, serverId, null, "drones", false, "tester");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => importer.PullAsync(projectId, serverId, null, "drones", false, "tester"));
        Assert.Contains("already been imported", refusal.Message, StringComparison.Ordinal);

        var listed = Assert.Single(await new IntakeService(db, new FakeModelGateway(), new PostgresArtifactStore(db),
            new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db)).ListAsync(projectId));
        Assert.Equal("drones", listed.Intake.Name);
        Assert.Equal(serverId, listed.Intake.SourceServerId);
        Assert.Equal(2, listed.Documents);
        Assert.Equal(0, listed.Extracted);
    }

    [Fact]
    public async Task DriftReportsWhatChangedWasAddedAndWasRemovedSinceThePull()
    {
        var docs = Corpus();
        var probe = CorpusServer(docs);
        var (projectId, serverId, _) = await RegisterAsync(probe);

        IntakeStarted started;
        await using (var db = fixture.NewDb())
        {
            started = await Importer(probe, db).PullAsync(projectId, serverId, null, null, false, "tester");
        }

        // At the source, after import: one edited, one new, one deleted —
        // and one new in an area this import never pulled, which is not its news.
        docs[1] = docs[1] with { Text = docs[1].Text + "\nThe reserve is computed from the weather too.\n" };
        docs.Add(new Doc("drones/0100-holding-the-circle", "draft", "# Holding the Circle\n"));
        docs.Add(new Doc("smashhit/0001-glass-fracture", "draft", "# Glass fracture\n"));
        docs.RemoveAt(0);

        CorpusDrift drift;
        await using (var db = fixture.NewDb())
        {
            drift = await Importer(probe, db).DriftAsync(started.Intake.Id);
        }

        var changed = Assert.Single(drift.Changed);
        Assert.Equal(Swarm, changed.OriginId);
        Assert.Equal(SpecGraphService.ComputeHash(docs[0].Text), changed.CurrentSha256);
        Assert.Equal("drones/0100-holding-the-circle", Assert.Single(drift.Added).Id);
        Assert.Equal(Northstar, Assert.Single(drift.Removed));
    }
}
