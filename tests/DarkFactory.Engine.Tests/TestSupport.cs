using DarkFactory.Core;
using DarkFactory.Data;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.Tests;

internal static class TestDb
{
    public static DarkFactoryDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<DarkFactoryDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}

/// <summary>Seeds a Project + WorkItem + Run, isolated per test by a unique id suffix.</summary>
internal static class TestSeed
{
    public static async Task<Run> SeedRunAsync(
        DarkFactoryDbContext db,
        string input = "add a health endpoint",
        StageId stage = StageId.Intake,
        RunStatus status = RunStatus.Pending)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        var project = new Project
        {
            Id = $"proj_{suffix}",
            OrgId = "org_test",
            Name = $"test-project-{suffix}",
            WorkspaceMcpUrl = $"http://example.invalid/{suffix}",
            CreatedAt = now,
        };
        db.Projects.Add(project);

        var workItem = new WorkItem
        {
            Id = $"wi_{suffix}",
            OrgId = "org_test",
            ProjectId = project.Id,
            Input = input,
            CreatedAt = now,
        };
        db.WorkItems.Add(workItem);

        var run = new Run
        {
            Id = $"run_{suffix}",
            OrgId = "org_test",
            ProjectId = project.Id,
            WorkItemId = workItem.Id,
            CurrentStage = stage,
            Status = status,
            CreatedAt = now,
        };
        db.Runs.Add(run);

        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>Directly inserts an artifact, for tests that seed a run partway through the pipeline.</summary>
    public static async Task<string> SeedArtifactAsync(DarkFactoryDbContext db, Run run, string type, string contentJson)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid().ToString("n"),
            OrgId = run.OrgId,
            ProjectId = run.ProjectId,
            RunId = run.Id,
            Type = type,
            ContentJson = contentJson,
            Sha256 = new string('0', 64),
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync();
        return ArtifactRef.Format(artifact.Id);
    }
}

internal static class PollUntil
{
    /// <summary>
    /// Bounded polling for an externally-driven condition (a subprocess
    /// advancing state in Postgres) to become true. Not a fixed
    /// Thread.Sleep-and-hope: it re-checks the real condition on a short
    /// interval and fails fast with a clear timeout once the bound is
    /// exceeded, so a genuinely broken resume shows up as a prompt test
    /// failure rather than a hang.
    /// </summary>
    public static async Task<T> AsyncCondition<T>(
        Func<Task<T?>> probe,
        Func<T, bool> isSatisfied,
        TimeSpan timeout,
        string description) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null && isSatisfied(result))
            {
                return result;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException($"Timed out after {timeout} waiting for: {description}");
    }
}
