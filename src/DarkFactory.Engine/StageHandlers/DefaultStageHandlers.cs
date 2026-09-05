using System.Text.Json;
using DarkFactory.Contracts;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Engine.StageHandlers;

internal static class ArtifactJson
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);

    public static async Task<string> LatestRefAsync(StageContext context, string artifactType, CancellationToken ct)
    {
        var artifact = await context.DbContext.Artifacts
            .Where(a => a.RunId == context.Run.Id && a.Type == artifactType)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return artifact is null
            ? throw new InvalidOperationException($"Run {context.Run.Id} has no '{artifactType}' artifact yet.")
            : Data.ArtifactRef.Format(artifact.Id);
    }
}

/// <summary>No artifact of its own — just confirms there's a work item to act on and moves to spec.</summary>
public sealed class IntakeStageHandler : IStageHandler
{
    public StageId Stage => StageId.Intake;

    public Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken) =>
        Task.FromResult<StageOutcome>(new StageOutcome.Success());
}

/// <summary>
/// Produces a Spec from the work item's raw input. A real implementation
/// calls the model with the theme's standards as context
/// (docs/adr/0009); this stub builds a deterministic, schema-valid Spec
/// directly so the engine's mechanics are exercisable without model access.
/// </summary>
public sealed class SpecStageHandler : IStageHandler
{
    public StageId Stage => StageId.Spec;

    public Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var spec = new Spec
        {
            ArtifactId = Guid.NewGuid().ToString("n"),
            RunId = context.Run.Id,
            Title = context.WorkItem.Input.Length <= 80 ? context.WorkItem.Input : context.WorkItem.Input[..80],
            Summary = context.WorkItem.Input,
            Requirements = [context.WorkItem.Input],
            AcceptanceCriteria = ["The change described in the work item's input is implemented and verified."],
            CreatedAt = now,
        };

        return Task.FromResult<StageOutcome>(new StageOutcome.Success(
            ArtifactType: "Spec",
            ArtifactContentJson: ArtifactJson.Serialize(spec),
            RequiresGate: GateKind.SpecApproval));
    }
}

/// <summary>Produces a Plan from the approved Spec.</summary>
public sealed class PlanStageHandler : IStageHandler
{
    public StageId Stage => StageId.Plan;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var specRef = await ArtifactJson.LatestRefAsync(context, "Spec", cancellationToken);

        var plan = new Plan
        {
            ArtifactId = Guid.NewGuid().ToString("n"),
            RunId = context.Run.Id,
            SpecArtifactRef = specRef,
            Steps = [new PlanStep { Order = 1, Description = "Implement the change described in the approved spec." }],
            TestStrategy = DefaultTheme.TestCommand,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return new StageOutcome.Success(ArtifactType: "Plan", ArtifactContentJson: ArtifactJson.Serialize(plan));
    }
}

/// <summary>
/// Produces a ChangeSet. A real implementation calls the workspace server's
/// files.*/vcs.* tools (docs/conventions/workspace.md) — that lands in step
/// 3 once the front MCP surface exists to broker the connection. This stub
/// records the plan it would have acted on so the pipeline mechanics are
/// exercisable end to end before that wiring exists.
/// </summary>
public sealed class ImplementStageHandler : IStageHandler
{
    public StageId Stage => StageId.Implement;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var planRef = await ArtifactJson.LatestRefAsync(context, "Plan", cancellationToken);

        var changeSet = new ChangeSet
        {
            ArtifactId = Guid.NewGuid().ToString("n"),
            RunId = context.Run.Id,
            PlanArtifactRef = planRef,
            Branch = $"dark-factory/{context.Run.Id}",
            PatchRef = "(stub: no workspace connection until step 3)",
            FilesChanged = [],
            CommitMessage = "Dark Factory: stub change (no workspace connection yet)",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return new StageOutcome.Success(ArtifactType: "ChangeSet", ArtifactContentJson: ArtifactJson.Serialize(changeSet));
    }
}

/// <summary>Produces a TestReport for the ChangeSet, then gates on PR approval before ship.</summary>
public sealed class VerifyStageHandler : IStageHandler
{
    public StageId Stage => StageId.Verify;

    public async Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken)
    {
        var changeSetRef = await ArtifactJson.LatestRefAsync(context, "ChangeSet", cancellationToken);

        var report = new TestReport
        {
            ArtifactId = Guid.NewGuid().ToString("n"),
            RunId = context.Run.Id,
            ChangesetArtifactRef = changeSetRef,
            Command = DefaultTheme.TestCommand,
            ExitCode = 0,
            Passed = 0,
            Failed = 0,
            Skipped = 0,
            Success = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        return new StageOutcome.Success(
            ArtifactType: "TestReport",
            ArtifactContentJson: ArtifactJson.Serialize(report),
            RequiresGate: GateKind.PrApproval);
    }
}

/// <summary>No artifact of its own — opening the PR is a vcs.open_pr call that lands with the workspace connection in step 3.</summary>
public sealed class ShipStageHandler : IStageHandler
{
    public StageId Stage => StageId.Ship;

    public Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken) =>
        Task.FromResult<StageOutcome>(new StageOutcome.Success());
}
