using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0037: the project owner's standing decisions about an import
/// are data on the intake, and every extraction is shown them — the
/// decision "live scope only" is made once, not re-made per document.
/// </summary>
[Collection("SpecGraph")]
public sealed class IntakeGuidanceTests(SpecGraphTestFixture fixture)
{
    private const string LiveScope = "Live scope only: documents marked deferred produce no nodes.";

    private static IntakeService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private static string Draft() => JsonSerializer.Serialize(new
    {
        reply = "Extracted.",
        draft = new
        {
            creates = new[] { new { kind = "rule", layer = "world", text = "A new game starts on Earth.", rationale = "Scope." } },
            revises = Array.Empty<object>(), retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(), edge_retires = Array.Empty<object>(),
        },
        holes = Array.Empty<object>(),
    });

    [Fact]
    public async Task EveryExtractionIsShownTheImportsGuidanceAndItsRecordedInThePack()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        var gateway = new FakeModelGateway().Responds(Draft()).Responds(Draft());

        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        var service = Service(gateway, db);
        var started = await service.StartAsync(projectId, "drones", [
            new("corpus:drones/0000-northstar", "Northstar", "# Northstar\n> A new game starts on Earth.\n"),
            new("corpus:drones/0070-alloys", "Alloys", "# Alloys\n> Deferred: the game in build is Earth only.\n"),
        ], "tester");

        await service.GuideAsync(started.Intake.Id, $"  {LiveScope}  ");
        var first = await service.ExtractAsync(started.Sources[0].Id, "tester");
        await service.ExtractAsync(started.Sources[1].Id, "tester");

        // In the cached half, identical for every document of the import.
        Assert.Contains(LiveScope, gateway.Requests[0].CacheableSystemPrefix, StringComparison.Ordinal);
        Assert.Equal(gateway.Requests[0].CacheableSystemPrefix, gateway.Requests[1].CacheableSystemPrefix);

        var artifact = await new PostgresArtifactStore(db).GetAsync(first.Source.ContextPackRef!);
        Assert.Equal(LiveScope, JsonSerializer.Deserialize<IntakeContextPack>(artifact!.ContentJson)!.Guidance);
    }

    [Fact]
    public async Task GuidanceReplacesRatherThanAppendsAndEmptyClearsIt()
    {
        var (projectId, _, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        var service = Service(new FakeModelGateway(), db);
        var started = await service.StartAsync(projectId, "drones",
            [new("corpus:drones/0000-northstar", "Northstar", "# Northstar\n")], "tester");

        await service.GuideAsync(started.Intake.Id, "first");
        await service.GuideAsync(started.Intake.Id, LiveScope);
        Assert.Equal(LiveScope, (await db.Intakes.AsNoTracking().SingleAsync(i => i.Id == started.Intake.Id)).Guidance);

        await service.GuideAsync(started.Intake.Id, "   ");
        Assert.Null((await db.Intakes.AsNoTracking().SingleAsync(i => i.Id == started.Intake.Id)).Guidance);
    }
}
