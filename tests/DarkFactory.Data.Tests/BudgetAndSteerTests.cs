using System.Text.Json;
using DarkFactory.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0028's budgets and docs/adr/0015's steering. Both are about a
/// run being answerable to a human while it is still running.
/// </summary>
[Collection("SpecGraph")]
public sealed class BudgetAndSteerTests(SpecGraphTestFixture fixture)
{
    private async Task<(Run Run, ResolvedAgent Agent)> SeedRunAsync(int? budget = null)
    {
        var (projectId, orgId, _) = await fixture.SeedProjectAsync();

        await using (var db = fixture.NewDb())
        {
            await new TeamService(db).SeedDefaultTeamAsync(projectId, orgId);
        }

        if (budget is not null)
        {
            await using var setBudget = fixture.NewDb();
            var member = await setBudget.TeamMembers.SingleAsync(m =>
                m.Role == AgentRoles.Implementer
                && setBudget.Teams.Any(t => t.Id == m.TeamId && t.ProjectId == projectId));
            member.TokenBudget = budget;
            await setBudget.SaveChangesAsync();
        }

        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        await using var db2 = fixture.NewDb();
        var workItem = new WorkItem
        {
            Id = $"wi_{suffix}", OrgId = orgId, ProjectId = projectId,
            Input = "budget test", CreatedAt = now,
        };
        db2.WorkItems.Add(workItem);

        var run = new Run
        {
            Id = $"run_{suffix}", OrgId = orgId, ProjectId = projectId, WorkItemId = workItem.Id,
            CurrentStage = StageId.Implement, Status = RunStatus.Running, CreatedAt = now,
        };
        db2.Runs.Add(run);
        await db2.SaveChangesAsync();

        var agent = await new TeamService(fixture.NewDb()).ResolveAsync(projectId, AssignmentPoints.Implement);
        return (run, agent);
    }

    // ---- budgets ----------------------------------------------------------

    /// <summary>
    /// Drives the real recording gateway rather than inserting rows.
    /// docs/adr/0032 says these rows are written by the gateway and never by
    /// an agent, so a test that wrote them itself would prove nothing about
    /// the thing the ADR actually promises.
    /// </summary>
    private RecordingModelGateway Recording(FakeModelGateway inner, DarkFactoryDbContext db) =>
        new(inner, db, NullLogger<RecordingModelGateway>.Instance);

    private static ModelRequest Call(ResolvedAgent agent, Run run, StageId stage, int attempt) =>
        new(agent.Deployment, "system", [new ModelMessage(ModelRole.User, "go")],
            Context: new ModelCallContext
            {
                OrgId = run.OrgId,
                ProjectId = run.ProjectId,
                RunId = run.Id,
                StageId = stage.ToString(),
                Attempt = attempt,
                TeamMemberId = agent.TeamMemberId,
            });

    [Fact]
    public async Task TheGatewayWritesAFactRowForEveryCall()
    {
        var (run, agent) = await SeedRunAsync();
        var inner = new FakeModelGateway().Responds("one").Responds("two").Responds("three");

        await using (var db = fixture.NewDb())
        {
            var gateway = Recording(inner, db);
            await gateway.CompleteAsync(Call(agent, run, StageId.Plan, 1));
            await gateway.CompleteAsync(Call(agent, run, StageId.Implement, 1));
            // A failed attempt still spent tokens. A counter that only moved
            // on success would never see this, which is exactly the spend a
            // budget exists to catch.
            await gateway.CompleteAsync(Call(agent, run, StageId.Implement, 2));
        }

        await using var verify = fixture.NewDb();
        var rows = await verify.ModelCalls.AsNoTracking()
            .Where(c => c.RunId == run.Id).OrderBy(c => c.CreatedAt).ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal([1, 1, 2], rows.Select(r => r.Attempt));
        Assert.Equal(["Plan", "Implement", "Implement"], rows.Select(r => r.StageId));
        Assert.All(rows, r =>
        {
            Assert.Equal(run.OrgId, r.OrgId);
            Assert.Equal(agent.TeamMemberId, r.TeamMemberId);
            Assert.Equal(agent.Deployment, r.Deployment);
            Assert.True(r.OutputTokens > 0);
        });

        // Budgets read from the facts, not a ledger (docs/adr/0032).
        Assert.Equal(450, await new BudgetService(verify).TotalForRunAsync(run.Id));
    }

    [Fact]
    public async Task CachedAndThinkingCountsAreBreakdownsNotAdditions()
    {
        var (run, agent) = await SeedRunAsync();

        // 1000 input of which 400 came from cache, 200 output of which 50
        // was thinking. Billable volume is 1200, not 1650.
        var inner = new FakeModelGateway().RespondsWithUsage(
            "ok", new ModelUsage(1000, 200, CachedInputTokens: 400, CacheWriteInputTokens: 30, ThinkingTokens: 50));

        await using (var db = fixture.NewDb())
        {
            await Recording(inner, db).CompleteAsync(Call(agent, run, StageId.Implement, 1));
        }

        await using var verify = fixture.NewDb();
        var row = await verify.ModelCalls.AsNoTracking().SingleAsync(c => c.RunId == run.Id);

        // The two halves of the input are stored apart because they are
        // billed apart — a cost model on totals alone flatters us.
        Assert.Equal(600, row.InputTokensUncached);
        Assert.Equal(400, row.InputTokensCached);
        Assert.Equal(30, row.CacheWriteTokens);
        Assert.Equal(200, row.OutputTokens);
        Assert.Equal(50, row.ThinkingTokens);

        Assert.Equal(1200, row.TotalTokens);
        Assert.Equal(1200, await new BudgetService(verify).TotalForRunAsync(run.Id));
    }

    [Fact]
    public async Task AFailedCallStillLeavesARow()
    {
        var (run, agent) = await SeedRunAsync();
        var inner = new FakeModelGateway().Fails("the provider said no");

        await using (var db = fixture.NewDb())
        {
            await Assert.ThrowsAsync<ModelGatewayException>(
                () => Recording(inner, db).CompleteAsync(Call(agent, run, StageId.Implement, 1)));
        }

        // "Which deployment fails most" is a question only these rows
        // answer, and a cost report that skips failures under-counts every
        // retry.
        await using var verify = fixture.NewDb();
        var row = await verify.ModelCalls.AsNoTracking().SingleAsync(c => c.RunId == run.Id);
        Assert.StartsWith("gateway_error:", row.StageResult!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCallerCompletesTheOutcomeColumnsItCouldNotKnowAtCallTime()
    {
        var (run, agent) = await SeedRunAsync();
        var inner = new FakeModelGateway().Responds("ok");

        await using (var db = fixture.NewDb())
        {
            var gateway = Recording(inner, db);
            var completion = await gateway.CompleteAsync(Call(agent, run, StageId.Implement, 1));

            Assert.NotNull(completion.ModelCallId);
            await gateway.RecordOutcomeAsync(
                completion.ModelCallId!, artifactValidFirstTry: true, stageResult: "succeeded");
        }

        await using var verify = fixture.NewDb();
        var row = await verify.ModelCalls.AsNoTracking().SingleAsync(c => c.RunId == run.Id);

        // Tokens alone rank the cheapest model best at everything. Tokens
        // beside "did it work first time" is what makes docs/adr/0022's
        // verifiability criterion measurable.
        Assert.True(row.ArtifactValidFirstTry);
        Assert.Equal("succeeded", row.StageResult);
    }

    [Fact]
    public async Task AMemberUnderBudgetIsNotExceeded()
    {
        var (run, agent) = await SeedRunAsync(budget: 1000);
        var inner = new FakeModelGateway().RespondsWithUsage("ok", new ModelUsage(400, 100));

        await using (var db = fixture.NewDb())
        {
            await Recording(inner, db).CompleteAsync(Call(agent, run, StageId.Implement, 1));
        }

        await using var verify = fixture.NewDb();
        var state = await new BudgetService(verify).StateAsync(run.Id, agent.TeamMemberId);

        Assert.Equal(500, state.Spent);
        Assert.Equal(1000, state.Limit);
        Assert.False(state.Exceeded);
        Assert.Equal(500, state.Remaining);
    }

    [Fact]
    public async Task DrivingAMemberPastItsBudgetParksTheStageForAHuman()
    {
        var (run, agent) = await SeedRunAsync(budget: 500);
        var inner = new FakeModelGateway().Responds("first").Responds("second");

        await using (var db = fixture.NewDb())
        {
            var gateway = Recording(inner, db);
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                await gateway.CompleteAsync(Call(agent, run, StageId.Implement, attempt));
            }
        }

        await using var verify = fixture.NewDb();
        Assert.False((await new BudgetService(verify).StateAsync(run.Id, agent.TeamMemberId)).Exceeded);

        // One more takes it over. The check is after the spend, because the
        // cost of a call is not knowable before it returns.
        await using (var db = fixture.NewDb())
        {
            await Recording(new FakeModelGateway().RespondsWithUsage("third", new ModelUsage(200, 100)), db)
                .CompleteAsync(Call(agent, run, StageId.Implement, 3));
        }

        await using var after = fixture.NewDb();
        var exceeded = await new BudgetService(after).StateAsync(run.Id, agent.TeamMemberId);

        Assert.True(exceeded.Exceeded);
        Assert.Equal(0, exceeded.Remaining);

        var message = BudgetService.OverBudgetMessage(agent.Role, exceeded);
        Assert.Contains("over its budget", message, StringComparison.Ordinal);
        // Retrying is the one thing that cannot help, so the class has to
        // be NeedsHuman rather than Retryable (docs/adr/0007).
        Assert.Contains("retrying will not help", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMemberWithNoBudgetIsNeverExceeded()
    {
        var (run, agent) = await SeedRunAsync();
        var inner = new FakeModelGateway().RespondsWithUsage("ok", new ModelUsage(999_999, 999_999));

        await using (var db = fixture.NewDb())
        {
            await Recording(inner, db).CompleteAsync(Call(agent, run, StageId.Implement, 1));
        }

        await using var verify = fixture.NewDb();
        var state = await new BudgetService(verify).StateAsync(run.Id, agent.TeamMemberId);

        Assert.Null(state.Limit);
        Assert.False(state.Exceeded);
    }

    [Fact]
    public async Task BudgetsAreCountedPerMemberNotPerRun()
    {
        var (run, implementer) = await SeedRunAsync(budget: 500);
        var planner = await new TeamService(fixture.NewDb()).ResolveAsync(run.ProjectId, AssignmentPoints.Plan);

        await using (var db = fixture.NewDb())
        {
            await Recording(new FakeModelGateway().RespondsWithUsage("p", new ModelUsage(900, 900)), db)
                .CompleteAsync(Call(planner, run, StageId.Plan, 1));
        }
        await using (var db = fixture.NewDb())
        {
            await Recording(new FakeModelGateway().RespondsWithUsage("i", new ModelUsage(100, 100)), db)
                .CompleteAsync(Call(implementer, run, StageId.Implement, 1));
        }

        await using var verify = fixture.NewDb();
        var budgets = new BudgetService(verify);

        // The planner burned far more, but it has no limit of its own, and
        // its spend must not park the implementer.
        Assert.False((await budgets.StateAsync(run.Id, implementer.TeamMemberId)).Exceeded);
        Assert.Equal(200, (await budgets.StateAsync(run.Id, implementer.TeamMemberId)).Spent);
        Assert.Equal(2000, await budgets.TotalForRunAsync(run.Id));
    }

    // ---- attach and steer -------------------------------------------------

    [Fact]
    public async Task AttachReturnsTheRunAndItsEventTail()
    {
        var (run, agent) = await SeedRunAsync();

        await using (var db = fixture.NewDb())
        {
            db.Events.Add(new Event
            {
                Id = Ulid.NewUlid(), RunId = run.Id, Type = "stage.completed",
                DataJson = """{"stage":"plan"}""", CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await Recording(new FakeModelGateway().RespondsWithUsage("x", new ModelUsage(10, 5)), db)
                .CompleteAsync(Call(agent, run, StageId.Plan, 1));
        }

        await using var db2 = fixture.NewDb();
        var view = await new RunObservationService(db2, new BudgetService(db2)).AttachAsync(run.Id);

        Assert.Equal(run.Id, view.RunId);
        Assert.Equal("Implement", view.Stage);
        Assert.Equal(15, view.TokensUsed);
        Assert.Contains(view.Events, e => e.Type == "stage.completed");
    }

    [Fact]
    public async Task SteeringRecordsAnEventThatTheNextStagesContextIncludes()
    {
        var (run, _) = await SeedRunAsync();

        await using (var db = fixture.NewDb())
        {
            await new RunObservationService(db, new BudgetService(db))
                .SteerAsync(run.Id, "Prefer the existing retry helper over a new one.", "rod");
        }

        // It is an ordinary event, so the dashboard and the audit trail get
        // it for free — and the outbox publisher will broadcast it.
        await using (var verify = fixture.NewDb())
        {
            var recorded = await verify.Events.AsNoTracking()
                .SingleAsync(e => e.RunId == run.Id && e.Type == RunObservationService.SteerEventType);
            Assert.Null(recorded.PublishedAt);
            Assert.Contains("retry helper", recorded.DataJson!, StringComparison.Ordinal);
        }

        // And the next stage actually sees it. A steer the next agent never
        // reads is just a log line.
        await using var db2 = fixture.NewDb();
        var observation = new RunObservationService(db2, new BudgetService(db2));
        var builder = new StageContextBuilder(
            db2, new PostgresArtifactStore(db2), new TeamService(db2), observation);

        var built = await builder.BuildAsync(run, StageId.Implement, attempt: 1);

        var steer = Assert.Single(built.Pack.Steers);
        Assert.Equal("Prefer the existing retry helper over a new one.", steer.Message);
        Assert.Equal("rod", steer.ActorId);

        // ...and the pack is persisted, like a conversational turn's.
        var artifact = await new PostgresArtifactStore(db2).GetAsync(built.Ref);
        Assert.NotNull(artifact);
        var round = JsonSerializer.Deserialize<StageContextPack>(artifact!.ContentJson)!;
        Assert.Single(round.Steers);
    }

    [Fact]
    public async Task SteersFromAnEarlierStageStillApplyLater()
    {
        var (run, _) = await SeedRunAsync();

        await using (var db = fixture.NewDb())
        {
            var observation = new RunObservationService(db, new BudgetService(db));
            await observation.SteerAsync(run.Id, "First thought.", "rod");
            await observation.SteerAsync(run.Id, "Second thought.", "rod");
        }

        await using var db2 = fixture.NewDb();
        var steers = await new RunObservationService(db2, new BudgetService(db2)).SteersForAsync(run.Id);

        // Guidance given during plan is still guidance during ship. Dropping
        // it at a stage boundary would make steering feel arbitrary.
        Assert.Equal(["First thought.", "Second thought."], steers.Select(s => s.Message));
    }

    [Fact]
    public async Task SteeringAFinishedRunIsRefused()
    {
        var (run, _) = await SeedRunAsync();

        await using (var db = fixture.NewDb())
        {
            var stored = await db.Runs.SingleAsync(r => r.Id == run.Id);
            stored.Status = RunStatus.Completed;
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.NewDb();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RunObservationService(db2, new BudgetService(db2)).SteerAsync(run.Id, "too late", "rod"));

        Assert.Contains("no next agent turn", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptySteerIsRefused()
    {
        var (run, _) = await SeedRunAsync();

        await using var db = fixture.NewDb();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new RunObservationService(db, new BudgetService(db)).SteerAsync(run.Id, "   ", "rod"));
    }
}
