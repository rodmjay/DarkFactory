using DarkFactory.Core;
using DarkFactory.Data;
using DarkFactory.Engine.StageHandlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DarkFactory.Engine.Tests;

// Drives a run through the full six-stage pipeline (docs/adr/0003) in one
// process, approving the two v1 gates (docs/adr/0003) as they're reached,
// and checks the artifact trail and event log come out right.
[Collection("Engine")]
public class RunStateMachineHappyPathTests(EngineTestFixture fixture)
{
    /// <summary>
    /// A fresh DbContext (and therefore a fresh change tracker) per call,
    /// matching how EngineWorker actually runs it in production — one DI
    /// scope per loop iteration. Reusing a single DbContext across multiple
    /// TryProcessOneAsync calls in a test would let EF's identity map hand
    /// back a stale tracked Run instance instead of what the claim query
    /// just wrote, masking exactly the kind of bug this suite exists to
    /// catch.
    /// </summary>
    private async Task<StageId?> ProcessOneAsync()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var stateMachine = new RunStateMachine(
            db,
            new RunLeaseStore(db),
            new PostgresArtifactStore(db),
            [
                new IntakeStageHandler(), new SpecStageHandler(), new PlanStageHandler(),
                new ImplementStageHandler(), new VerifyStageHandler(), new ShipStageHandler(),
            ],
            Options.Create(new EngineOptions { WorkerId = "test-worker", LeaseDurationSeconds = 30 }),
            NullLogger<RunStateMachine>.Instance);

        return await stateMachine.TryProcessOneAsync();
    }

    [Fact]
    public async Task Run_completes_all_six_stages_when_both_gates_are_approved()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db, input: "add a GET /health/detailed endpoint");

        // intake
        Assert.Equal(StageId.Intake, await ProcessOneAsync());
        // spec -> parks on the spec-approval gate
        Assert.Equal(StageId.Spec, await ProcessOneAsync());

        var afterSpec = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.AwaitingApproval, afterSpec.Status);
        Assert.Equal(StageId.Spec, afterSpec.CurrentStage);

        await using (var db1 = TestDb.Create(fixture.ConnectionString))
        {
            await new GateService(db1).ApproveAsync(run.Id, GateKind.SpecApproval);
        }

        // plan, implement
        Assert.Equal(StageId.Plan, await ProcessOneAsync());
        Assert.Equal(StageId.Implement, await ProcessOneAsync());
        // verify -> parks on the PR-approval gate
        Assert.Equal(StageId.Verify, await ProcessOneAsync());

        var afterVerify = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.AwaitingApproval, afterVerify.Status);
        Assert.Equal(StageId.Verify, afterVerify.CurrentStage);

        await using (var db2 = TestDb.Create(fixture.ConnectionString))
        {
            await new GateService(db2).ApproveAsync(run.Id, GateKind.PrApproval);
        }

        // ship
        Assert.Equal(StageId.Ship, await ProcessOneAsync());

        var final = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Completed, final.Status);
        Assert.Equal(StageId.Ship, final.CurrentStage);
        Assert.Null(final.LeasedBy);

        var artifactTypes = await db.Artifacts.Where(a => a.RunId == run.Id).Select(a => a.Type).ToListAsync();
        Assert.Equal(["Spec", "Plan", "ChangeSet", "TestReport"], artifactTypes);

        var completedStages = await db.Events
            .Where(e => e.RunId == run.Id && e.Type == "stage.completed")
            .CountAsync();
        Assert.Equal(6, completedStages); // intake, spec, plan, implement, verify, ship

        var gateEvents = await db.Events
            .Where(e => e.RunId == run.Id && (e.Type == "gate.waiting" || e.Type == "gate.approved"))
            .CountAsync();
        Assert.Equal(4, gateEvents); // waiting+approved, twice
    }

    [Fact]
    public async Task Rejecting_a_gate_fails_the_run()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db);

        await ProcessOneAsync(); // intake
        await ProcessOneAsync(); // spec -> gate

        await using (var db1 = TestDb.Create(fixture.ConnectionString))
        {
            await new GateService(db1).RejectAsync(run.Id, GateKind.SpecApproval, "not what we asked for");
        }

        var final = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(RunStatus.Failed, final.Status);
    }
}
