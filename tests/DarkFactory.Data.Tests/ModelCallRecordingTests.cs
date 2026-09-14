using DarkFactory.Core;
using DarkFactory.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0032: usage fact rows are written by the gateway and never by
/// an agent. That is a claim about <em>wiring</em> as much as about code —
/// a decorator nobody is handed protects nothing — so these tests check
/// both halves: that the container hands out the recorder, and that the
/// recorder records what the response actually contained.
/// </summary>
[Collection("SpecGraph")]
public sealed class ModelCallRecordingTests(SpecGraphTestFixture fixture)
{
    private static ServiceProvider BuildContainer(string connectionString, string provider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DarkFactory"] = connectionString,
                [ModelGatewayRegistration.ProviderKey] = provider,
                // Never used: no call is made through the real provider here.
                ["Anthropic:ApiKey"] = "not-a-real-key",
                ["Foundry:Endpoint"] = "https://example.openai.azure.com/",
                ["Foundry:ApiKey"] = "not-a-real-key",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDarkFactoryData(configuration);
        services.AddModelGateway(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Anthropic")]
    [InlineData("Foundry")]
    public void TheContainerHandsOutTheRecorderNotTheProvider(string provider)
    {
        using var container = BuildContainer(fixture.OwnerConnectionString, provider);
        using var scope = container.CreateScope();

        // The thing that makes docs/adr/0032 structural: there is no
        // registration that yields a bare provider, so no caller can make a
        // model call that escapes the ledger — on either provider.
        Assert.IsType<RecordingModelGateway>(scope.ServiceProvider.GetRequiredService<IModelGateway>());
    }

    [Fact]
    public void TheSameInstanceAnswersTheOutcomeLog()
    {
        using var container = BuildContainer(fixture.OwnerConnectionString, "Anthropic");
        using var scope = container.CreateScope();

        // A caller completing an outcome must annotate the row it already
        // caused to be written, not a second one from a different recorder.
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<IModelGateway>(),
            scope.ServiceProvider.GetRequiredService<IModelCallLog>());
    }

    [Fact]
    public async Task OneCallWritesExactlyOneRowWithTheResponsesBreakdown()
    {
        var (run, agent) = await SeedAsync();

        var inner = new FakeModelGateway().RespondsWithUsage("ok", new ModelUsage(
            InputTokens: 1000, OutputTokens: 300,
            CachedInputTokens: 700, CacheWriteInputTokens: 120, ThinkingTokens: 90));

        await using (var db = fixture.NewDb())
        {
            await Recorder(inner, db).CompleteAsync(Call(agent, run, attempt: 1));
        }

        await using var verify = fixture.NewDb();
        var row = Assert.Single(await verify.ModelCalls.AsNoTracking()
            .Where(c => c.RunId == run.Id).ToListAsync());

        // Populated from the response, not from anything the caller passed —
        // the caller does not know what the cache did.
        Assert.Equal(300, row.InputTokensUncached);
        Assert.Equal(700, row.InputTokensCached);
        Assert.Equal(120, row.CacheWriteTokens);
        Assert.Equal(300, row.OutputTokens);
        Assert.Equal(90, row.ThinkingTokens);
        Assert.Equal(12, row.LatencyMs);
        Assert.Equal("fake", row.Provider);
        Assert.Equal("fake-model", row.ModelFamily);

        // And the dimensions the caller did supply.
        Assert.Equal(run.Id, row.RunId);
        Assert.Equal(agent.TeamMemberId, row.TeamMemberId);
        Assert.Equal("factory://artifacts/ctx", row.ContextPackRef);
        Assert.Equal("architect/1", row.PromptTemplateVersion);
        Assert.Equal(["skill@v1"], row.SkillRevisions);

        // No listed price for a fake model, so no invented cost.
        Assert.Null(row.Cost);
    }

    [Fact]
    public async Task ACallToAPricedModelRecordsWhatItCost()
    {
        var (run, agent) = await SeedAsync();
        var inner = new FakeModelGateway().RespondsWithUsage("ok", new ModelUsage(
            InputTokens: 1000, OutputTokens: 300, CachedInputTokens: 700, CacheWriteInputTokens: 120), "claude-opus-5");

        await using (var db = fixture.NewDb())
        {
            await Recorder(inner, db).CompleteAsync(Call(agent, run, attempt: 1));
        }

        await using var verify = fixture.NewDb();
        var row = Assert.Single(await verify.ModelCalls.AsNoTracking().Where(c => c.RunId == run.Id).ToListAsync());
        Assert.Equal(0.0095m, row.Cost);
    }

    [Fact]
    public async Task ARetryProducesASecondRowAtAttemptTwo()
    {
        var (run, agent) = await SeedAsync();
        var inner = new FakeModelGateway().Responds("first").Responds("second");

        await using (var db = fixture.NewDb())
        {
            var recorder = Recorder(inner, db);
            await recorder.CompleteAsync(Call(agent, run, attempt: 1));
            await recorder.CompleteAsync(Call(agent, run, attempt: 2, retried: true));
        }

        await using var verify = fixture.NewDb();
        var rows = await verify.ModelCalls.AsNoTracking()
            .Where(c => c.RunId == run.Id).OrderBy(c => c.Attempt).ToListAsync();

        // Two rows, not one updated in place. A retry that burned tokens
        // before failing is the spend a budget exists to catch, and a
        // single row per stage would hide it.
        Assert.Equal(2, rows.Count);
        Assert.Equal([1, 2], rows.Select(r => r.Attempt));
        Assert.Null(rows[0].Retried);
        Assert.True(rows[1].Retried);
    }

    // ---- harness ----------------------------------------------------------

    private static RecordingModelGateway Recorder(FakeModelGateway inner, DarkFactoryDbContext db) =>
        new(inner, db, NullLogger<RecordingModelGateway>.Instance);

    private static ModelRequest Call(ResolvedAgent agent, Run run, int attempt, bool? retried = null) =>
        new(agent.Deployment, "variable", [new ModelMessage(ModelRole.User, "go")],
            Context: new ModelCallContext
            {
                OrgId = run.OrgId,
                ProjectId = run.ProjectId,
                RunId = run.Id,
                StageId = StageId.Implement.ToString(),
                Attempt = attempt,
                TeamMemberId = agent.TeamMemberId,
                ContextPackRef = "factory://artifacts/ctx",
                PromptTemplateVersion = "architect/1",
                SkillRevisions = ["skill@v1"],
                Retried = retried,
            },
            CacheableSystemPrefix: "stable");

    private async Task<(Run Run, ResolvedAgent Agent)> SeedAsync()
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();
        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        await using var db2 = fixture.NewDb();
        db2.WorkItems.Add(new WorkItem
        {
            Id = $"wi_{suffix}", OrgId = orgId, ProjectId = projectId, Input = "recording", CreatedAt = now,
        });
        var run = new Run
        {
            Id = $"run_{suffix}", OrgId = orgId, ProjectId = projectId, WorkItemId = $"wi_{suffix}",
            CurrentStage = StageId.Implement, Status = RunStatus.Running, CreatedAt = now,
        };
        db2.Runs.Add(run);
        await db2.SaveChangesAsync();

        return (run, await new TeamService(fixture.NewDb()).ResolveAsync(projectId, AssignmentPoints.Implement));
    }
}
