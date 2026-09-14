using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0039: a question is a decision, so it arrives with the paths
/// open to whoever answers it. What is under test is what the factory keeps
/// and refuses — 2 to 4 options, at most one recommended, ids its own — and
/// that a suggestion never answers anything.
/// </summary>
[Collection("SpecGraph")]
public sealed class IntakeOptionsTests(SpecGraphTestFixture fixture)
{
    private const string SwarmRef = "corpus:specs/0079-the-working-swarm.md";
    private const string NorthstarRef = "corpus:specs/0000-northstar.md";
    private const string Reserve = "An autonomous drone turns back when its return reserve is reached.";

    private static IntakeService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private async Task<IntakeStarted> StartAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        return await Service(new FakeModelGateway(), db).StartAsync(projectId, "drones", [
            new(NorthstarRef, "Northstar", "One world, shared by everybody."),
            new(SwarmRef, "The Working Swarm", "The autopilot always reserves enough energy to get home."),
        ], "tester");
    }

    private static string SwarmId(IntakeStarted started) => started.Sources.Single(s => s.SourceRef == SwarmRef).Id;

    private static object Option(string label, bool recommended = false) =>
        new { label, consequence = $"Commits to {label.ToLowerInvariant()}.", recommended };

    private static string Response(params object[] holes) => JsonSerializer.Serialize(new
    {
        reply = "Extracted.",
        draft = new
        {
            creates = new[] { new { kind = "rule", layer = "swarm", text = Reserve, rationale = "The cycle." } },
            revises = Array.Empty<object>(), retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(), edge_retires = Array.Empty<object>(),
        },
        holes,
    });

    private static object Hole(string question, object[]? options = null) =>
        new { id = (string?)null, kind = "ambiguity", question, quote = "enough energy", affects = new[] { 0 }, options };

    private static IntakeOption[] OptionsOf(IntakeQuestion q) =>
        q.OptionsJson is null ? [] : JsonSerializer.Deserialize<IntakeOption[]>(q.OptionsJson)!;

    private async Task<List<IntakeQuestion>> QuestionsAsync(string intakeId)
    {
        await using var db = fixture.NewDb();
        return await db.IntakeQuestions.AsNoTracking().Where(q => q.IntakeId == intakeId).OrderBy(q => q.Question).ToListAsync();
    }

    [Fact]
    public async Task ExtractionKeepsTheOptionsAHoleArrivesWithAndNumbersThemItself()
    {
        var started = await StartAsync();
        await using var db = fixture.NewDb();
        await Service(new FakeModelGateway().Responds(Response(
            Hole("Distance alone, or conditions too?", [Option("Distance alone"), Option("Distance and wind", recommended: true)]))), db)
            .ExtractAsync(SwarmId(started), "tester");

        var options = OptionsOf((await QuestionsAsync(started.Intake.Id)).Single());
        Assert.Equal(["o1", "o2"], options.Select(o => o.Id));
        Assert.Equal("Distance and wind", options.Single(o => o.Recommended).Label);
        Assert.Equal("Commits to distance alone.", options[0].Consequence);
    }

    [Theory]
    [InlineData(1, 0)] // one option is not a choice
    [InlineData(5, 0)] // five is a survey
    [InlineData(3, 2)] // two recommendations are none
    public async Task OptionsOutsideTheContractAreRefusedAndTheCorrectedRetryKept(int count, int recommended)
    {
        var started = await StartAsync();
        var bad = Enumerable.Range(1, count).Select(i => Option($"Path {i}", recommended: i <= recommended)).ToArray();
        var gateway = new FakeModelGateway()
            .Responds(Response(Hole("Distance alone, or conditions too?", bad)))
            .Responds(Response(Hole("Distance alone, or conditions too?", [Option("A"), Option("B")])));

        await using var db = fixture.NewDb();
        await Service(gateway, db).ExtractAsync(SwarmId(started), "tester");

        Assert.Contains("/holes/0/options", gateway.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Equal(2, OptionsOf((await QuestionsAsync(started.Intake.Id)).Single()).Length);
    }

    [Fact]
    public async Task SuggestingPathsFillsOnlyOpenQuestionsWithoutThemAndAnswersNothing()
    {
        var started = await StartAsync();
        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway().Responds(Response(
                Hole("A: distance alone?"),
                Hole("B: who sees the map?"),
                Hole("C: already has paths?", [Option("Yes"), Option("No")]))), db)
                .ExtractAsync(SwarmId(started), "tester");
        }

        var before = await QuestionsAsync(started.Intake.Id);
        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway(), db).AnswerAsync(before[1].Id, "Everyone.", "tester");
        }

        var open = before[0];
        var gateway = new FakeModelGateway().Responds(JsonSerializer.Serialize(new
        {
            paths = new[] { new { question_id = open.Id, options = new[] { Option("Distance"), Option("Conditions too") } } },
        }));
        await using (var db = fixture.NewDb())
        {
            var result = await Service(gateway, db).SuggestPathsAsync(started.Intake.Id);
            Assert.Equal(1, result.QuestionsUpdated);
        }

        // One call, about the one question that needed paths, shown the
        // scope document alongside its own.
        var request = Assert.Single(gateway.Requests);
        Assert.Contains(open.Id, request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(before[1].Id, request.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(before[2].Id, request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("One world, shared by everybody.", request.SystemPrompt, StringComparison.Ordinal);

        var after = await QuestionsAsync(started.Intake.Id);
        Assert.Equal(["Distance", "Conditions too"], OptionsOf(after[0]).Select(o => o.Label));
        Assert.Equal(IntakeQuestionStatus.Open, after[0].Status);
        Assert.Empty(OptionsOf(after[1]));
        Assert.Equal(["Yes", "No"], OptionsOf(after[2]).Select(o => o.Label));
    }

    [Fact]
    public async Task PathsThatStillFailAfterTheRetryLeaveTheQuestionAnswerableInOnesOwnWords()
    {
        var started = await StartAsync();
        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway().Responds(Response(Hole("Distance alone?"))), db)
                .ExtractAsync(SwarmId(started), "tester");
        }
        var id = (await QuestionsAsync(started.Intake.Id)).Single().Id;
        var one = JsonSerializer.Serialize(new { paths = new[] { new { question_id = id, options = new[] { Option("Only") } } } });
        var gateway = new FakeModelGateway().Responds(one).Responds(one);

        await using (var db = fixture.NewDb())
        {
            Assert.Equal(0, (await Service(gateway, db).SuggestPathsAsync(started.Intake.Id)).QuestionsUpdated);
        }

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Null((await QuestionsAsync(started.Intake.Id)).Single().OptionsJson);
    }
}
