using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0037: corpus intake. What is under test is the factory's side
/// of the loop — what an extraction may and may not persist, what blocks a
/// proposal, and what the model is shown — never the quality of any
/// model's reading of a document.
/// </summary>
[Collection("SpecGraph")]
public sealed class IntakeServiceTests(SpecGraphTestFixture fixture)
{
    private const string SwarmRef = "corpus:specs/0079-the-working-swarm.md";
    private const string NorthstarRef = "corpus:specs/0000-northstar.md";
    private const string Swarm = "The drones you are not flying run the whole cycle themselves. The autopilot always reserves enough energy to get home.";
    private const string Northstar = "One world, shared by everybody. The map starts black and opens only where somebody has flown.";

    private static IntakeService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private async Task<(string ProjectId, IntakeStarted Started)> StartAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);

        // Submitted out of order on purpose: corpus order is the factory's
        // decision, not the caller's.
        var started = await Service(new FakeModelGateway(), db).StartAsync(projectId, "drones", [
            new(SwarmRef, "The Working Swarm", Swarm),
            new(NorthstarRef, "Northstar", Northstar),
        ], "tester");

        return (projectId, started);
    }

    private static string SwarmId(IntakeStarted started) =>
        started.Sources.Single(s => s.SourceRef == SwarmRef).Id;

    private async Task<IntakeExtraction> ExtractAsync(FakeModelGateway gateway, string sourceId)
    {
        await using var db = fixture.NewDb();
        return await Service(gateway, db).ExtractAsync(sourceId, "tester");
    }

    private static object Draft(params string[] texts) => new
    {
        creates = texts.Select(t => new { kind = "rule", layer = "swarm", text = t, rationale = "The cycle." }).ToArray(),
        revises = Array.Empty<object>(),
        retires = Array.Empty<object>(),
        edge_adds = Array.Empty<object>(),
        edge_retires = Array.Empty<object>(),
    };

    private static object Hole(string question, string? id = null, int[]? affects = null, string kind = "ambiguity") =>
        new { id, kind, question, quote = "enough energy to get home", affects = affects ?? [0] };

    private static string Response(object draft, params object[] holes) =>
        JsonSerializer.Serialize(new { reply = "Extracted.", draft, holes });

    private const string Reserve = "An autonomous drone turns back when its return reserve is reached.";
    private const string ReserveQuestion = "Is the return reserve computed from distance alone, or from conditions too?";

    // ---- extraction -------------------------------------------------------

    [Fact]
    public async Task ExtractionDraftsNodesAndRaisesQuestionsWithoutTouchingTheGraph()
    {
        var (projectId, started) = await StartAsync();
        var gateway = new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion)));

        var result = await ExtractAsync(gateway, SwarmId(started));

        Assert.True(result.Accepted);
        Assert.Equal(IntakeSourceStatus.Extracted, result.Source.Status);
        Assert.Equal(1, result.Source.DraftRevision);
        var question = Assert.Single(result.OpenQuestions);
        Assert.Equal(ReserveQuestion, question.Question);
        Assert.Equal(1, question.RaisedInRevision);

        // Every node names where it came from, whatever the model wrote.
        var draft = JsonSerializer.Deserialize<SpecDiffDocument>(result.Source.DraftJson!)!;
        Assert.Contains(SwarmRef, Assert.Single(draft.Creates).Rationale, StringComparison.Ordinal);

        // A draft is not the graph, and not an amendment either.
        await using var verify = fixture.NewDb();
        Assert.Equal(0, await verify.SpecNodes.CountAsync(n => n.ProjectId == projectId));
        Assert.Equal(0, await verify.Amendments.CountAsync(a => a.ProjectId == projectId));
    }

    [Fact]
    public async Task EveryExtractionSeesTheWholeCorpusInOneCacheablePrefix()
    {
        var (_, started) = await StartAsync();

        // Northstar sorts first even though it was submitted second.
        Assert.Equal([NorthstarRef, SwarmRef], started.Sources.OrderBy(s => s.Seq).Select(s => s.SourceRef));

        var gateway = new FakeModelGateway()
            .Responds(Response(Draft("The map starts black.")))
            .Responds(Response(Draft(Reserve)));

        foreach (var source in started.Sources.OrderBy(s => s.Seq))
        {
            Assert.True((await ExtractAsync(gateway, source.Id)).Accepted);
        }

        var first = gateway.Requests[0];
        var second = gateway.Requests[1];

        // Both documents are in what the model saw...
        Assert.Contains(Northstar, first.CacheableSystemPrefix, StringComparison.Ordinal);
        Assert.Contains(Swarm, first.CacheableSystemPrefix, StringComparison.Ordinal);

        // ...in a prefix that is byte-identical across extractions, which is
        // the whole reason it can be cached rather than paid for per document.
        Assert.Equal(first.CacheableSystemPrefix, second.CacheableSystemPrefix);

        // The target is named in the half that varies.
        Assert.Contains(NorthstarRef, first.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(SwarmRef, second.SystemPrompt, StringComparison.Ordinal);

        // And the second is shown the layer the first chose.
        Assert.Contains("swarm", second.SystemPrompt.Split("## Layers already in use")[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADraftThatChangesTheGraphIsRetriedOnceThenRefusedWithNothingPersisted()
    {
        var (projectId, started) = await StartAsync();

        // A node that really exists, so the only thing that can refuse the
        // revision is intake's own rule — not the reference check, which
        // would reject an invented id for a different reason and let this
        // test pass with the rule deleted.
        await Graph.ApplyAsync(fixture, projectId, started.Intake.OrgId, started.Intake.ConversationId,
            Graph.Diff(creates: [Graph.Create("The map starts black.")]));
        string existing;
        await using (var db = fixture.NewDb())
        {
            existing = await db.SpecNodes.Where(n => n.ProjectId == projectId).Select(n => n.SpecId).SingleAsync();
        }

        var reviser = JsonSerializer.Serialize(new
        {
            reply = "Revised an existing node.",
            draft = new
            {
                creates = Array.Empty<object>(),
                revises = new[] { new { spec_id = existing, text = "Something else." } },
                retires = Array.Empty<object>(),
                edge_adds = Array.Empty<object>(),
                edge_retires = Array.Empty<object>(),
            },
            holes = Array.Empty<object>(),
        });
        var gateway = new FakeModelGateway().Responds(reviser).Responds(reviser);

        var result = await ExtractAsync(gateway, SwarmId(started));

        Assert.False(result.Accepted);
        Assert.Equal(2, gateway.Requests.Count);
        Assert.Contains("only creates nodes", gateway.Requests[1].Messages[^1].Content, StringComparison.Ordinal);

        await using var verify = fixture.NewDb();
        var source = await verify.IntakeSources.SingleAsync(s => s.Id == SwarmId(started));
        Assert.Equal(IntakeSourceStatus.Failed, source.Status);
        Assert.Null(source.DraftJson);
        Assert.Contains("only creates nodes", source.Failure, StringComparison.Ordinal);
        Assert.Equal(0, await verify.IntakeQuestions.CountAsync(q => q.SourceId == source.Id));
    }

    [Fact]
    public async Task AHolePointingPastTheDraftIsRefusedAndTheCorrectedRetryKept()
    {
        var (_, started) = await StartAsync();
        var gateway = new FakeModelGateway()
            .Responds(Response(Draft(Reserve), Hole(ReserveQuestion, affects: [3])))
            .Responds(Response(Draft(Reserve), Hole(ReserveQuestion, affects: [0])));

        var result = await ExtractAsync(gateway, SwarmId(started));

        Assert.True(result.Accepted);
        Assert.Contains("/holes/0/affects", gateway.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Single(result.OpenQuestions);
    }

    [Fact]
    public async Task ADocumentReadsBackWithItsTextItsDraftAndItsQuestions()
    {
        var (_, started) = await StartAsync();
        await ExtractAsync(new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));

        await using var db = fixture.NewDb();
        var detail = await Service(new FakeModelGateway(), db).GetSourceAsync(SwarmId(started));

        Assert.Equal(Swarm, detail.Source.Content);
        Assert.Equal(Reserve, Assert.Single(detail.Draft!.Creates).Text);
        Assert.Equal(ReserveQuestion, Assert.Single(detail.Questions).Question);
    }

    // ---- what blocks a proposal -------------------------------------------

    [Fact]
    public async Task ProposingIsRefusedWhileAQuestionIsOpen()
    {
        var (_, started) = await StartAsync();
        await ExtractAsync(new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));

        await using var db = fixture.NewDb();
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(new FakeModelGateway(), db).ProposeAsync(SwarmId(started)));
        Assert.Contains("1 open question", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerMustBeInTheDraftBeforeTheDraftCanBeProposed()
    {
        var (_, started) = await StartAsync();
        var extracted = await ExtractAsync(
            new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));

        await using var db = fixture.NewDb();
        var service = Service(new FakeModelGateway(), db);
        await service.AnswerAsync(extracted.OpenQuestions[0].Id, "From distance and the weather.", "rod");

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProposeAsync(SwarmId(started)));
        Assert.Contains("does not reflect yet", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReExtractionBuildsTheAnswerInAndTheResultIsProposedNotApplied()
    {
        var (projectId, started) = await StartAsync();
        var extracted = await ExtractAsync(
            new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));
        var questionId = extracted.OpenQuestions[0].Id;

        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway(), db).AnswerAsync(questionId, "From distance and the weather.", "rod");
        }

        const string Revised = "The return reserve is computed from the distance home and the current weather.";
        var gateway = new FakeModelGateway().Responds(Response(Draft(Reserve, Revised)));
        var reExtracted = await ExtractAsync(gateway, SwarmId(started));

        // The model was told the answer and shown its own previous draft.
        Assert.Contains("From distance and the weather.", gateway.Requests[0].SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(Reserve, gateway.Requests[0].SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(2, reExtracted.Source.DraftRevision);
        Assert.Empty(reExtracted.OpenQuestions);

        Amendment amendment;
        await using (var db = fixture.NewDb())
        {
            var question = await db.IntakeQuestions.SingleAsync(q => q.Id == questionId);
            Assert.Equal(IntakeQuestionStatus.Answered, question.Status);
            Assert.Equal(2, question.IncorporatedInRevision);

            amendment = await Service(new FakeModelGateway(), db).ProposeAsync(SwarmId(started));
        }

        // Proposed, under the intake's conversation, and nothing more: the
        // graph moves on approval, which intake does not do.
        await using var verify = fixture.NewDb();
        Assert.Equal(AmendmentStatus.Proposed, amendment.Status);
        Assert.Equal(started.Intake.ConversationId, amendment.ConversationId);
        Assert.Contains(Revised, amendment.DiffJson, StringComparison.Ordinal);
        Assert.Equal(0, await verify.SpecNodes.CountAsync(n => n.ProjectId == projectId));

        var source = await verify.IntakeSources.SingleAsync(s => s.Id == SwarmId(started));
        Assert.Equal(IntakeSourceStatus.Proposed, source.Status);
        Assert.Equal(amendment.Id, source.AmendmentId);

        // And once proposed, intake is finished with it.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(new FakeModelGateway(), verify).ExtractAsync(SwarmId(started), "tester"));
    }

    [Fact]
    public async Task AnOpenQuestionTheNextExtractionStopsRaisingIsResolvedAndOneItKeepsStaysOpen()
    {
        var (_, started) = await StartAsync();
        var first = await ExtractAsync(new FakeModelGateway().Responds(Response(
            Draft(Reserve, "Idle drones say why."),
            Hole(ReserveQuestion),
            Hole("Does 'idle' include charging?", affects: [1]))), SwarmId(started));

        var kept = first.OpenQuestions.Single(q => q.Question == ReserveQuestion);
        var dropped = first.OpenQuestions.Single(q => q.Question != ReserveQuestion);

        // The reserve question is carried by id, now pointing at the node's
        // new position; the idle question is not raised again.
        var second = await ExtractAsync(new FakeModelGateway().Responds(Response(
            Draft("Idle drones say why.", Reserve),
            Hole(ReserveQuestion, id: kept.Id, affects: [1]))), SwarmId(started));

        Assert.Equal(kept.Id, Assert.Single(second.OpenQuestions).Id);

        await using var verify = fixture.NewDb();
        var questions = await verify.IntakeQuestions.Where(q => q.SourceId == SwarmId(started)).ToListAsync();
        Assert.Equal(2, questions.Count);
        Assert.Equal("[1]", questions.Single(q => q.Id == kept.Id).AffectsJson);
        Assert.Equal(IntakeQuestionStatus.Resolved, questions.Single(q => q.Id == dropped.Id).Status);
    }

    [Fact]
    public async Task AnsweredQuestionsCannotBeRaisedAgain()
    {
        var (_, started) = await StartAsync();
        var extracted = await ExtractAsync(
            new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));
        var questionId = extracted.OpenQuestions[0].Id;

        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway(), db).AnswerAsync(questionId, "From distance and the weather.", "rod");
        }

        var echo = Response(Draft(Reserve), Hole(ReserveQuestion, id: questionId));
        var gateway = new FakeModelGateway().Responds(echo).Responds(echo);
        var result = await ExtractAsync(gateway, SwarmId(started));

        Assert.False(result.Accepted);
        Assert.Contains(result.Errors, e => e.Location == "/holes/0/id");

        // The previous accepted draft survives a refused re-extraction.
        Assert.Equal(1, result.Source.DraftRevision);
        Assert.Equal(IntakeSourceStatus.Extracted, result.Source.Status);
    }

    [Fact]
    public async Task DeferringAQuestionUnblocksTheProposalWithoutAnotherExtraction()
    {
        var (_, started) = await StartAsync();
        var extracted = await ExtractAsync(
            new FakeModelGateway().Responds(Response(Draft(Reserve), Hole(ReserveQuestion))), SwarmId(started));

        await using var db = fixture.NewDb();
        var service = Service(new FakeModelGateway(), db);
        await service.DeferAsync(extracted.OpenQuestions[0].Id, "Tune it in playtesting.", "rod");

        var amendment = await service.ProposeAsync(SwarmId(started));
        Assert.Equal(AmendmentStatus.Proposed, amendment.Status);
    }
}
