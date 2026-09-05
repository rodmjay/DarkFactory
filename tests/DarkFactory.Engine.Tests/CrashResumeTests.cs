using System.Diagnostics;
using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Tests;

/// <summary>
/// The flagship durability test for docs/adr/0008: a real, separate OS
/// process is killed mid-run and a second, independent process resumes it
/// from durable Postgres state alone — no shared memory, no coordination
/// between the two beyond what's committed to the database. This is what
/// actually proves the checkpoint/lease/outbox design, as opposed to
/// exercising the same C# objects across simulated "restarts."
/// </summary>
[Collection("Engine")]
public class CrashResumeTests(EngineTestFixture fixture)
{
    [Fact]
    public async Task Killing_the_worker_right_after_implement_resumes_at_verify_without_reprocessing_implement()
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        var run = await TestSeed.SeedRunAsync(db, stage: StageId.Implement, status: RunStatus.Pending);
        var specRef = await TestSeed.SeedArtifactAsync(db, run, "Spec",
            $$"""{"artifact_id":"spec1","run_id":"{{run.Id}}","title":"t","summary":"s","requirements":["r"],"acceptance_criteria":["a"],"created_at":"2024-01-01T00:00:00Z"}""");
        await TestSeed.SeedArtifactAsync(db, run, "Plan",
            $$"""{"artifact_id":"plan1","run_id":"{{run.Id}}","spec_artifact_ref":"{{specRef}}","steps":[{"order":1,"description":"do it"}],"test_strategy":"echo ok","created_at":"2024-01-01T00:00:00Z"}""");

        // --- Act 1: run a real worker process, told to crash right after it commits `implement`'s checkpoint. ---
        using var proc1 = StartEngineProcess(workerId: "crash-proc-1", crashAfterStage: "Implement");
        var exited = await WaitForExitAsync(proc1, TimeSpan.FromSeconds(20));
        Assert.True(exited, "worker process did not exit within the timeout — crash injection may not have fired");
        Assert.Equal(137, proc1.ExitCode);

        // --- Assert: implement is durably done, exactly once, and the run already moved on to verify — ---
        // --- all committed atomically before the process died.                                          ---
        var afterCrash = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(StageId.Verify, afterCrash.CurrentStage);
        Assert.Null(afterCrash.LeasedBy);

        Assert.Equal(1, await db.StageCheckpoints.CountAsync(c => c.RunId == run.Id && c.Stage == StageId.Implement));
        Assert.Equal(1, await db.Artifacts.CountAsync(a => a.RunId == run.Id && a.Type == "ChangeSet"));
        Assert.Equal(1, await db.Events.CountAsync(e =>
            e.RunId == run.Id && e.Type == "stage.completed" && e.DataJson!.Contains("\"stage\":\"Implement\"")));

        // --- Act 2: a brand new, independent process — the "restart." No crash flag this time. ---
        using var proc2 = StartEngineProcess(workerId: "crash-proc-2", crashAfterStage: null);
        try
        {
            await PollUntil.AsyncCondition(
                probe: () => FetchRunAsync(run.Id),
                isSatisfied: r => r.Status == RunStatus.AwaitingApproval,
                timeout: TimeSpan.FromSeconds(20),
                description: "restarted worker finishes verify and parks on the PR-approval gate");
        }
        finally
        {
            KillIfRunning(proc2);
        }

        // --- Assert: verify ran exactly once, in the new process — and implement was never touched again. ---
        var final = await db.Runs.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.Equal(StageId.Verify, final.CurrentStage);
        Assert.Equal(RunStatus.AwaitingApproval, final.Status);

        Assert.Equal(1, await db.StageCheckpoints.CountAsync(c => c.RunId == run.Id && c.Stage == StageId.Implement));
        Assert.Equal(1, await db.Artifacts.CountAsync(a => a.RunId == run.Id && a.Type == "ChangeSet"));
        Assert.Equal(1, await db.Events.CountAsync(e =>
            e.RunId == run.Id && e.Type == "stage.completed" && e.DataJson!.Contains("\"stage\":\"Implement\"")));

        Assert.Equal(1, await db.StageCheckpoints.CountAsync(c => c.RunId == run.Id && c.Stage == StageId.Verify));
        Assert.Equal(1, await db.Artifacts.CountAsync(a => a.RunId == run.Id && a.Type == "TestReport"));
    }

    private Process StartEngineProcess(string workerId, string? crashAfterStage)
    {
        var startInfo = new ProcessStartInfo("dotnet", $"\"{Path.Combine(fixture.EnginePublishDir, "DarkFactory.Engine.dll")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        startInfo.Environment["ConnectionStrings__DarkFactory"] = fixture.ConnectionString;
        startInfo.Environment["Engine__WorkerId"] = workerId;
        // The durability mechanics are what is under test here, not the
        // agents: stub handlers keep this test free of a model provider
        // and a workspace server, either of which would make a crash
        // test depend on something that is not the crash.
        startInfo.Environment["Engine__StageHandlers"] = "stub";
        startInfo.Environment["Engine__LeaseDurationSeconds"] = "30";
        startInfo.Environment["Engine__PollIntervalSeconds"] = "0.2";
        if (crashAfterStage is not null)
        {
            startInfo.Environment["DARKFACTORY_CRASH_AFTER_STAGE"] = crashAfterStage;
        }

        var process = Process.Start(startInfo)!;
        // Drain the pipes so the child never blocks trying to write to a full buffer.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return process;
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<Run?> FetchRunAsync(string runId)
    {
        await using var db = TestDb.Create(fixture.ConnectionString);
        return await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId);
    }

    private static void KillIfRunning(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }
}
