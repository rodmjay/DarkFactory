using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// The read side of the conversation, which the dashboard renders a thread
/// from. What matters is that a thread reads back as it was answered — the
/// stored payloads, not a reconstruction — and that where an amendment
/// stands, including why it was refused, comes with it.
/// </summary>
[Collection("SpecGraph")]
public sealed class ConversationReadTests(SpecGraphTestFixture fixture)
{
    private static ConversationService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private async Task<(string ProjectId, string OrgId, string ConversationId, string AmendmentId)> ProposeAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);

        var gateway = new FakeModelGateway()
            .RespondsProposing(FakeModelGateway.CreateDiff("The map starts black."), "Proposing it.");
        var service = Service(gateway, db);
        var conversation = await service.StartAsync(projectId, "The map", "tester");
        var turn = await service.TurnAsync(conversation.Id, "The map starts black.", "tester");

        return (projectId, orgId, conversation.Id, turn.AmendmentId!);
    }

    [Fact]
    public async Task AThreadReadsBackWithItsStoredPayloadsAndWhereItsAmendmentStands()
    {
        var (_, _, conversationId, amendmentId) = await ProposeAsync();

        await using var db = fixture.NewDb();
        var thread = await Service(new FakeModelGateway(), db).GetAsync(conversationId);

        Assert.Equal([TurnRole.User, TurnRole.Assistant], thread.Turns.Select(t => t.Role));
        Assert.Equal("The map starts black.", thread.Turns[0].Content);

        // The architect's turn carries the spec_diff it was answered with.
        var payloads = thread.Turns[1].PayloadsJson!;
        Assert.Contains("\"type\":\"spec_diff\"", payloads, StringComparison.Ordinal);
        Assert.Contains(amendmentId, payloads, StringComparison.Ordinal);

        var amendment = Assert.Single(thread.Amendments);
        Assert.Equal(amendmentId, amendment.Id);
        Assert.Equal(AmendmentStatus.Proposed, amendment.Status);
        Assert.Equal(1, thread.Listing.AwaitingAmendments);
        Assert.Equal(2, thread.Listing.TurnCount);
    }

    [Fact]
    public async Task ARejectionKeepsItsReason()
    {
        var (_, _, conversationId, amendmentId) = await ProposeAsync();

        await using (var db = fixture.NewDb())
        {
            await new SpecGraphService(db).RejectAsync(amendmentId, "rod", "Solo worlds are private; say so.");
        }

        await using var verify = fixture.NewDb();
        var amendment = Assert.Single((await Service(new FakeModelGateway(), verify).GetAsync(conversationId)).Amendments);
        Assert.Equal(AmendmentStatus.Rejected, amendment.Status);
        Assert.Equal("Solo worlds are private; say so.", amendment.RejectedReason);
    }

    [Fact]
    public async Task TheListPutsTheMostRecentlyActiveFirstAndMarksAnIntake()
    {
        var (projectId, _, conversationId, _) = await ProposeAsync();

        IntakeStarted intake;
        await using (var db = fixture.NewDb())
        {
            intake = await new IntakeService(db, new FakeModelGateway(), new PostgresArtifactStore(db),
                    new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db))
                .StartAsync(projectId, "drones", [new("corpus:a.md", "A", "The map starts black.")], "tester");
        }

        await using var verify = fixture.NewDb();
        var listings = await Service(new FakeModelGateway(), verify).ListAsync(projectId);

        // Newest activity first: the intake was started after the proposal.
        Assert.Equal(intake.Intake.ConversationId, listings[0].Conversation.Id);
        Assert.True(listings[0].IsIntake);

        var discussed = listings.Single(l => l.Conversation.Id == conversationId);
        Assert.False(discussed.IsIntake);
        Assert.Equal(1, discussed.Amendments);
        Assert.Equal(1, discussed.AwaitingAmendments);
        Assert.All(listings, l => Assert.Equal(projectId, l.Conversation.ProjectId));
    }
}
