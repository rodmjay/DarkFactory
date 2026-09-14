using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0041: what the architect needs a person to decide arrives as a
/// decision with its paths, not as questions buried in prose. Under test is
/// what the factory keeps — valid decisions become payloads, invalid ones are
/// dropped without costing the reply — and that the architect is told to
/// answer this way at all.
/// </summary>
[Collection("SpecGraph")]
public sealed class ConversationDecisionTests(SpecGraphTestFixture fixture)
{
    private ConversationService Service(FakeModelGateway gateway, DarkFactoryDbContext db) =>
        new(db, gateway, new PostgresArtifactStore(db), new TeamService(db), new SpecGraphService(db), new SpecDiffTranslator(db));

    private async Task<string> SeedAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using var db = fixture.NewDb();
        await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        return (await Service(new FakeModelGateway(), db).StartAsync(projectId, "test", "tester")).Id;
    }

    private static string Reply(params object[] decisions) => JsonSerializer.Serialize(new
    {
        reply = "Two things decide the scope.",
        decisions,
        settled = false,
        diff = (object?)null,
    });

    private static object Decision(string title, params (string Label, bool Recommended)[] options) => new
    {
        title,
        why = "It changes what gets extracted.",
        options = options.Select(o => new { label = o.Label, consequence = $"Commits to {o.Label}.", recommended = o.Recommended }).ToArray(),
    };

    private async Task<(ConversationTurnResult Result, FakeModelGateway Gateway)> TurnAsync(string reply)
    {
        var conversationId = await SeedAsync();
        var gateway = new FakeModelGateway().Responds(reply);
        await using var db = fixture.NewDb();
        return (await Service(gateway, db).TurnAsync(conversationId, "what's missing?", "tester"), gateway);
    }

    [Fact]
    public async Task ADecisionTheArchitectNeedsArrivesWithItsPathsOpen()
    {
        var (result, _) = await TurnAsync(Reply(Decision("Earth only, or several maps?", ("Earth only", true), ("Several maps", false))));

        Assert.Equal("Two things decide the scope.", Assert.IsType<MarkdownPayload>(result.Payloads[0]).Text);
        var decision = Assert.IsType<DecisionPayload>(Assert.Single(result.Payloads.Skip(1)));
        Assert.Equal("Earth only, or several maps?", decision.Title);
        Assert.Equal(["o1", "o2"], decision.Options.Select(o => o.Id));
        Assert.Equal("Earth only", decision.Options.Single(o => o.Recommended).Label);
        Assert.True(decision.AllowOther && decision.AllowDefer);

        // Stored as the thread will read it back.
        await using var db = fixture.NewDb();
        var stored = await db.Turns.AsNoTracking().SingleAsync(t => t.Id == result.TurnId);
        Assert.Contains("\"type\":\"decision\"", stored.PayloadsJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADecisionOutsideTheContractIsDroppedAndTheReplyKept()
    {
        var (result, gateway) = await TurnAsync(Reply(
            Decision("One path is not a choice", ("Only", false)),
            Decision("Two recommendations are none", ("A", true), ("B", true)),
            Decision("Who shares the world?", ("Everybody", false), ("Friends", false))));

        Assert.Single(gateway.Requests); // a bad decision is not worth a retry
        Assert.IsType<MarkdownPayload>(result.Payloads[0]);
        Assert.Equal("Who shares the world?", Assert.IsType<DecisionPayload>(Assert.Single(result.Payloads.Skip(1))).Title);
    }

    [Fact]
    public async Task NoMoreThanThreeDecisionsAreKept()
    {
        var many = Enumerable.Range(1, 5).Select(i => Decision($"Question {i}?", ("Yes", false), ("No", false))).ToArray();
        var (result, _) = await TurnAsync(Reply(many));

        Assert.Equal(["Question 1?", "Question 2?", "Question 3?"], result.Payloads.OfType<DecisionPayload>().Select(d => d.Title));
    }

    [Fact]
    public async Task TheArchitectIsToldToAskInDecisionsAndKeepItsProseShort()
    {
        var (_, gateway) = await TurnAsync(Reply());
        var prefix = gateway.Requests[0].CacheableSystemPrefix!;

        Assert.Contains("\"decisions\"", prefix, StringComparison.Ordinal);
        Assert.Contains("three sentences or fewer", prefix, StringComparison.Ordinal);
    }
}
