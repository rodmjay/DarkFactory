using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0017: the conversation is the product. What is under test here
/// is the factory's behaviour around the model — what gets recorded, what
/// gets validated, and what refuses to be persisted — never the model's.
/// </summary>
[Collection("SpecGraph")]
public sealed class ConversationServiceTests(SpecGraphTestFixture fixture)
{
    private ConversationService Service(FakeModelGateway gateway, DarkFactoryDbContext db)
    {
        var specs = new SpecGraphService(db);
        return new ConversationService(
            db, gateway, new PostgresArtifactStore(db), new TeamService(db), specs, new SpecDiffTranslator(db));
    }

    private async Task<(string ProjectId, string OrgId, string ConversationId)> SeedAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);

        var conversation = await Service(new FakeModelGateway(), db)
            .StartAsync(projectId, "test", "tester");

        return (projectId, orgId, conversation.Id);
    }

    // ---- constraint 2: what the model saw is a stored artifact -----------

    [Fact]
    public async Task TheTurnRecordsExactlyWhatTheModelWasShown()
    {
        var (projectId, _, conversationId) = await SeedAsync();
        var gateway = new FakeModelGateway().RespondsChatting("Noted.");

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "Renewals should be blocked when delinquent.", "tester");
        }

        Assert.NotNull(result.ContextRef);
        Assert.StartsWith("factory://artifacts/", result.ContextRef, StringComparison.Ordinal);

        await using var verify = fixture.NewDb();

        // The assistant turn points at the pack...
        var turn = await verify.Turns.AsNoTracking().SingleAsync(t => t.Id == result.TurnId);
        Assert.Equal(result.ContextRef, turn.RetrievalRef);
        Assert.Equal(150, turn.TokenUsage);

        // ...and the pack is really there, with the contents the prompt was
        // built from. "Why did it say that" is a lookup, not an inference.
        var artifact = await new PostgresArtifactStore(verify).GetAsync(result.ContextRef);
        Assert.NotNull(artifact);
        Assert.Equal(ConversationService.ContextPackArtifactType, artifact!.Type);
        Assert.Equal(conversationId, artifact.ConversationId);
        Assert.Null(artifact.RunId);

        var pack = JsonSerializer.Deserialize<ContextPack>(artifact.ContentJson)!;
        Assert.Equal(conversationId, pack.ConversationId);
        Assert.Equal(projectId, pack.ProjectId);
        Assert.Equal(AgentRoles.Architect, pack.Agent.Role);
        Assert.NotEmpty(pack.Standards);
        Assert.NotEmpty(pack.Skills);
        Assert.Equal(1, pack.Attempt);

        // The user's message was in the tail the model saw.
        Assert.Contains(pack.ConversationTail, t => t.Content.Contains("delinquent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheContextPackCapturesTheSpecGraphAsItWasAtThatMoment()
    {
        var (projectId, orgId, conversationId) = await SeedAsync();

        await Graph.ApplyAsync(fixture, projectId, orgId, conversationId,
            Graph.Diff(creates: [Graph.Create("Invoices are immutable once issued.")]));

        var gateway = new FakeModelGateway().RespondsChatting();
        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "What do we say about invoices?", "tester");
        }

        await using var verify = fixture.NewDb();
        var artifact = await new PostgresArtifactStore(verify).GetAsync(result.ContextRef);
        var pack = JsonSerializer.Deserialize<ContextPack>(artifact!.ContentJson)!;

        var node = Assert.Single(pack.SpecNeighborhood);
        Assert.Equal("Invoices are immutable once issued.", node.Text);
        Assert.False(node.Retired);
        Assert.NotEmpty(node.RevisionHash);

        // And the prompt actually carried it — a pack recording context the
        // model never received would be a comfortable lie.
        Assert.Contains("Invoices are immutable once issued.", gateway.Requests[0].SystemPrompt, StringComparison.Ordinal);
        Assert.Contains(node.SpecId, gateway.Requests[0].SystemPrompt, StringComparison.Ordinal);
    }

    // ---- constraint 1: deployment names only ------------------------------

    [Fact]
    public async Task TheTurnIsRoutedToTheDeploymentTheProjectsTeamNames()
    {
        var (projectId, _, conversationId) = await SeedAsync();

        await using (var db = fixture.NewDb())
        {
            var member = await db.TeamMembers
                .SingleAsync(m => m.Role == AgentRoles.Architect
                    && db.Teams.Any(t => t.Id == m.TeamId && t.ProjectId == projectId));
            member.Deployment = "architect-gpt5-eastus";
            await db.SaveChangesAsync();
        }

        var gateway = new FakeModelGateway().RespondsChatting();
        await using (var db = fixture.NewDb())
        {
            await Service(gateway, db).TurnAsync(conversationId, "hello", "tester");
        }

        // The service asked for a deployment by name and nothing else. No
        // endpoint, no key, no model id — docs/adr/0027.
        Assert.Equal("architect-gpt5-eastus", Assert.Single(gateway.Requests).Deployment);
    }

    [Fact]
    public async Task AProjectWithNoTeamFailsClearlyRatherThanPickingAModel()
    {
        var (projectId, _, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        var conversation = await Service(new FakeModelGateway(), db).StartAsync(projectId, null, "tester");

        var ex = await Assert.ThrowsAsync<TeamNotConfiguredException>(
            () => Service(new FakeModelGateway(), db).TurnAsync(conversation.Id, "hi", "tester"));

        Assert.Contains("no active team", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- constraint 3: parsed, never trusted ------------------------------

    [Fact]
    public async Task AValidProposalBecomesAnAmendment()
    {
        var (projectId, _, conversationId) = await SeedAsync();
        var gateway = new FakeModelGateway()
            .RespondsProposing(FakeModelGateway.CreateDiff("Renewal is blocked if the account is delinquent."));

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "Block renewal when delinquent.", "tester");
        }

        Assert.NotNull(result.AmendmentId);
        Assert.Single(gateway.Requests); // no retry needed

        var payload = Assert.IsType<SpecDiffPayload>(result.Payloads.Last());
        Assert.Equal(result.AmendmentId, payload.AmendmentId);
        Assert.Equal("creates 1", payload.Summary);

        await using var verify = fixture.NewDb();
        var amendment = await verify.Amendments.AsNoTracking().SingleAsync(a => a.Id == result.AmendmentId);
        Assert.Equal(AmendmentStatus.Proposed, amendment.Status);
        Assert.Equal(projectId, amendment.ProjectId);
    }

    [Fact]
    public async Task AnInvalidProposalIsRetriedOnceWithTheValidationErrors()
    {
        var (_, _, conversationId) = await SeedAsync();

        var gateway = new FakeModelGateway()
            // First attempt: 'directive' is not one of the schema's kinds.
            .RespondsProposing(new
            {
                creates = new[] { new { kind = "directive", layer = "domain", text = "Something." } },
                revises = Array.Empty<object>(),
                retires = Array.Empty<object>(),
                edge_adds = Array.Empty<object>(),
                edge_retires = Array.Empty<object>(),
            })
            // Second: corrected.
            .RespondsProposing(FakeModelGateway.CreateDiff("Something."));

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "add a rule", "tester");
        }

        Assert.Equal(2, gateway.Requests.Count);
        Assert.NotNull(result.AmendmentId);

        // The retry told the model precisely what was wrong, rather than
        // "that was invalid, try again".
        var retryMessage = gateway.Requests[1].Messages.Last().Content;
        Assert.Contains("/creates/0/kind", retryMessage, StringComparison.Ordinal);
        Assert.Contains("enum", retryMessage, StringComparison.Ordinal);

        // Both attempts recorded their own context pack.
        await using var verify = fixture.NewDb();
        var packs = await verify.Artifacts.AsNoTracking()
            .Where(a => a.ConversationId == conversationId
                && a.Type == ConversationService.ContextPackArtifactType)
            .ToListAsync();
        Assert.Equal(2, packs.Count);
        Assert.Equal([1, 2], packs
            .Select(p => JsonSerializer.Deserialize<ContextPack>(p.ContentJson)!.Attempt)
            .OrderBy(a => a));
    }

    [Fact]
    public async Task TwoInvalidAttemptsProposeNothingAndSaySo()
    {
        var (projectId, _, conversationId) = await SeedAsync();

        object badDiff = new
        {
            creates = new[] { new { kind = "nonsense", layer = "domain", text = "x" } },
            revises = Array.Empty<object>(),
            retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(),
            edge_retires = Array.Empty<object>(),
        };

        var gateway = new FakeModelGateway()
            .RespondsProposing(badDiff)
            .RespondsProposing(badDiff);

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "add a rule", "tester");
        }

        Assert.Equal(2, gateway.Requests.Count);

        // No invalid amendment reaches df.specs.propose. Not a partial one,
        // not a "best effort" one — none.
        Assert.Null(result.AmendmentId);
        await using var verify = fixture.NewDb();
        Assert.Empty(await verify.Amendments.AsNoTracking().Where(a => a.ProjectId == projectId).ToListAsync());

        // And the user is told, rather than left to assume it worked.
        Assert.All(result.Payloads, p => Assert.IsType<MarkdownPayload>(p));
        var text = string.Join("\n", result.Payloads.Cast<MarkdownPayload>().Select(p => p.Text));
        Assert.Contains("could not express that as a valid amendment", text, StringComparison.Ordinal);

        // The turn is still persisted: a failed proposal is part of the
        // conversation's history, not something to hide.
        Assert.NotNull(await verify.Turns.AsNoTracking().SingleOrDefaultAsync(t => t.Id == result.TurnId));
    }

    [Fact]
    public async Task ResponseThatIsNotJsonIsTreatedAsAValidationFailure()
    {
        var (_, _, conversationId) = await SeedAsync();
        var gateway = new FakeModelGateway()
            .Responds("Sure! Here's what I think we should do about renewals.")
            .RespondsChatting("Sorry — here it is properly.");

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "hello", "tester");
        }

        Assert.Equal(2, gateway.Requests.Count);
        Assert.Null(result.AmendmentId);
        Assert.Equal("Sorry — here it is properly.", Assert.IsType<MarkdownPayload>(result.Payloads[0]).Text);
    }

    [Fact]
    public async Task AFencedJsonResponseIsAccepted()
    {
        var (_, _, conversationId) = await SeedAsync();
        var gateway = new FakeModelGateway().Responds(
            "```json\n{\"reply\":\"Understood.\",\"settled\":false,\"diff\":null}\n```");

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "hi", "tester");
        }

        // A fence is not a correctness problem — the content still has to
        // validate — so rejecting it would be pedantry.
        Assert.Single(gateway.Requests);
        Assert.Equal("Understood.", Assert.IsType<MarkdownPayload>(Assert.Single(result.Payloads)).Text);
    }

    [Fact]
    public async Task AHallucinatedSpecIdIsCaughtBeforeAnythingIsPersisted()
    {
        var (projectId, _, conversationId) = await SeedAsync();

        // Shaped exactly like a real ULID, and completely made up. The
        // schema cannot tell; only the database can.
        const string invented = "01ARZ3NDEKTSV4RRFFQ69G5FAV";

        object diff = new
        {
            creates = Array.Empty<object>(),
            revises = new[] { new { spec_id = invented, text = "Revised text." } },
            retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(),
            edge_retires = Array.Empty<object>(),
        };

        var gateway = new FakeModelGateway().RespondsProposing(diff).RespondsProposing(diff);

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "change that rule", "tester");
        }

        Assert.Null(result.AmendmentId);
        Assert.Contains(invented, gateway.Requests[1].Messages.Last().Content, StringComparison.Ordinal);
        Assert.Contains("does not exist in this project",
            gateway.Requests[1].Messages.Last().Content, StringComparison.Ordinal);

        await using var verify = fixture.NewDb();
        Assert.Empty(await verify.Amendments.AsNoTracking().Where(a => a.ProjectId == projectId).ToListAsync());
    }

    [Fact]
    public async Task AnEmptyDiffIsNotAProposal()
    {
        var (projectId, _, conversationId) = await SeedAsync();

        object empty = new
        {
            creates = Array.Empty<object>(),
            revises = Array.Empty<object>(),
            retires = Array.Empty<object>(),
            edge_adds = Array.Empty<object>(),
            edge_retires = Array.Empty<object>(),
        };

        var gateway = new FakeModelGateway().RespondsProposing(empty).RespondsProposing(empty);

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "ok", "tester");
        }

        Assert.Null(result.AmendmentId);
        await using var verify = fixture.NewDb();
        Assert.Empty(await verify.Amendments.AsNoTracking().Where(a => a.ProjectId == projectId).ToListAsync());
    }

    [Fact]
    public async Task AnUnsettledTurnProposesNothingAndDoesNotRetry()
    {
        var (projectId, _, conversationId) = await SeedAsync();
        var gateway = new FakeModelGateway().RespondsChatting("Which accounts count as delinquent?");

        ConversationTurnResult result;
        await using (var db = fixture.NewDb())
        {
            result = await Service(gateway, db).TurnAsync(conversationId, "block renewals", "tester");
        }

        // The ordinary case, and it must not look like a failure: one call,
        // no amendment, no apology.
        Assert.Single(gateway.Requests);
        Assert.Null(result.AmendmentId);
        var payload = Assert.IsType<MarkdownPayload>(Assert.Single(result.Payloads));
        Assert.Equal("Which accounts count as delinquent?", payload.Text);
        Assert.DoesNotContain("could not", payload.Text, StringComparison.OrdinalIgnoreCase);

        await using var verify = fixture.NewDb();
        Assert.Empty(await verify.Amendments.AsNoTracking().Where(a => a.ProjectId == projectId).ToListAsync());
    }

    [Fact]
    public async Task TurnsAreSequencedAndBothSidesArePersisted()
    {
        var (_, _, conversationId) = await SeedAsync();

        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway().RespondsChatting("One."), db)
                .TurnAsync(conversationId, "first", "tester");
        }
        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway().RespondsChatting("Two."), db)
                .TurnAsync(conversationId, "second", "tester");
        }

        await using var verify = fixture.NewDb();
        var turns = await verify.Turns.AsNoTracking()
            .Where(t => t.ConversationId == conversationId)
            .OrderBy(t => t.Seq).ToListAsync();

        Assert.Equal([1, 2, 3, 4], turns.Select(t => t.Seq));
        Assert.Equal(
            [TurnRole.User, TurnRole.Assistant, TurnRole.User, TurnRole.Assistant],
            turns.Select(t => t.Role));
        Assert.Equal("second", turns[2].Content);
    }

    [Fact]
    public async Task TheSecondTurnSeesTheFirstInItsContext()
    {
        var (_, _, conversationId) = await SeedAsync();

        await using (var db = fixture.NewDb())
        {
            await Service(new FakeModelGateway().RespondsChatting("Noted."), db)
                .TurnAsync(conversationId, "Renewals matter to us.", "tester");
        }

        var gateway = new FakeModelGateway().RespondsChatting("Still noted.");
        await using (var db = fixture.NewDb())
        {
            await Service(gateway, db).TurnAsync(conversationId, "As I was saying.", "tester");
        }

        var messages = Assert.Single(gateway.Requests).Messages;
        Assert.Contains(messages, m => m.Content == "Renewals matter to us." && m.Role == ModelRole.User);
        Assert.Contains(messages, m => m.Content == "Noted." && m.Role == ModelRole.Assistant);
    }
}
