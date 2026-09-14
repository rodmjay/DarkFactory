using System.Text.Json;
using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0039: the factory, not the person, knows what to do next. What
/// is under test is the order of the steps — scope document first, and
/// within a document decide, rebuild, propose — and that the walk ends with
/// every document's specs pending, never approved.
/// </summary>
[Collection("SpecGraph")]
public sealed class IntakeNextTests(SpecGraphTestFixture fixture)
{
    private const string NorthstarRef = "corpus:specs/0000-northstar.md";
    private const string SwarmRef = "corpus:specs/0079-the-working-swarm.md";
    private const string AlloysRef = "corpus:specs/0070-alloys.md";

    private static IntakeService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private async Task<IntakeStarted> StartAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        return await Service(new FakeModelGateway(), db).StartAsync(projectId, "drones", [
            new(SwarmRef, "The Working Swarm", "The autopilot always reserves enough energy to get home."),
            new(NorthstarRef, "Northstar", "One world, shared by everybody."),
            new(AlloysRef, "Alloys", "Deferred: the game in build is Earth only."),
        ], "tester");
    }

    private static string Id(IntakeStarted started, string sourceRef) =>
        started.Sources.Single(s => s.SourceRef == sourceRef).Id;

    private static string Response(int nodes, params string[] holes) => JsonSerializer.Serialize(new
    {
        reply = "Extracted.",
        draft = new
        {
            creates = Enumerable.Range(0, nodes)
                .Select(i => new { kind = "rule", layer = "world", text = $"Rule {i}.", rationale = "Scope." }).ToArray(),
            revises = Array.Empty<object>(), retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(), edge_retires = Array.Empty<object>(),
        },
        holes = holes.Select(h => new { id = (string?)null, kind = "ambiguity", question = h, quote = "x", affects = Array.Empty<int>() }),
    });

    private async Task<T> With<T>(Func<IntakeService, Task<T>> act, FakeModelGateway? gateway = null)
    {
        await using var db = fixture.NewDb();
        return await act(Service(gateway ?? new FakeModelGateway(), db));
    }

    private Task<IntakeNextStep> NextAsync(IntakeStarted started) => With(s => s.NextAsync(started.Intake.Id));

    [Fact]
    public async Task TheWalkGoesScopeFirstThenDecideRebuildProposeAndEndsWithEverythingPending()
    {
        var started = await StartAsync();
        var northstar = Id(started, NorthstarRef);
        var swarm = Id(started, SwarmRef);
        var alloys = Id(started, AlloysRef);

        // Read in the wrong order on purpose: the walk's order is corpus order.
        await With(s => s.ExtractAsync(swarm, "tester"), new FakeModelGateway().Responds(Response(1, "Distance alone?")));
        await With(s => s.ExtractAsync(alloys, "tester"), new FakeModelGateway().Responds(Response(0)));
        await With(s => s.ExtractAsync(northstar, "tester"), new FakeModelGateway().Responds(Response(2, "Who shares the world?")));

        var step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Decide, northstar), (step.Step, step.Source!.Id));
        Assert.Equal("Who shares the world?", step.Question!.Question);
        Assert.Equal(new IntakeProgress(3, 1, 2, 2), step.Progress); // alloys yielded nothing and asked nothing

        await With(s => s.AnswerAsync(step.Question.Id, "Everybody.", "tester"));
        step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Rebuild, northstar), (step.Step, step.Source!.Id));

        await With(s => s.ExtractAsync(northstar, "tester"), new FakeModelGateway().Responds(Response(2)));
        step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Propose, northstar, 2), (step.Step, step.Source!.Id, step.Nodes));

        await With(s => s.ProposeAsync(northstar));
        step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Decide, swarm), (step.Step, step.Source!.Id));

        await With(s => s.DeferAsync(step.Question!.Id, "Later.", "tester"));
        step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Propose, swarm), (step.Step, step.Source!.Id));

        var amendment = await With(s => s.ProposeAsync(swarm));
        step = await NextAsync(started);
        Assert.Equal(IntakeSteps.Done, step.Step);
        Assert.Equal(new IntakeProgress(3, 3, 0, 0), step.Progress);

        // Proposed is pending: nothing reached the graph without approval.
        Assert.Equal(AmendmentStatus.Proposed, amendment.Status);
    }

    [Fact]
    public async Task ADocumentNotYetReadNeverBlocksOneThatIsReady()
    {
        var started = await StartAsync();
        var swarm = Id(started, SwarmRef);
        await With(s => s.ExtractAsync(swarm, "tester"), new FakeModelGateway().Responds(Response(1)));

        var step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Propose, swarm), (step.Step, step.Source!.Id));

        await With(s => s.ProposeAsync(swarm));
        step = await NextAsync(started);
        Assert.Equal((IntakeSteps.Extract, Id(started, NorthstarRef)), (step.Step, step.Source!.Id));
    }
}
