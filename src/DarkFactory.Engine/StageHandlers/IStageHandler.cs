using DarkFactory.Core;
using DarkFactory.Data;

namespace DarkFactory.Engine.StageHandlers;

public sealed record StageContext(Run Run, WorkItem WorkItem, DarkFactoryDbContext DbContext);

/// <summary>
/// What a stage produced. A base record with two cases rather than an
/// interface + booleans, so a handler can't forget to specify a failure
/// class on failure (docs/adr/0007) — the type system requires one.
/// </summary>
public abstract record StageOutcome
{
    public sealed record Success(
        string? ArtifactType = null,
        string? ArtifactContentJson = null,
        GateKind? RequiresGate = null,
        // A ChangeSet is a unified diff and a PR body is markdown; both get
        // served over HTTP to a workspace server, which needs them as text
        // rather than wrapped in JSON.
        string ArtifactContentType = ArtifactContentTypes.Json,
        // Token usage attributed to this stage, for the per-run budget
        // (docs/adr/0028). Zero for stages that call no model.
        int InputTokens = 0,
        int OutputTokens = 0,
        string? TeamMemberId = null,
        string? Deployment = null) : StageOutcome;

    public sealed record Failed(FailureClass FailureClass, string Message) : StageOutcome;
}

/// <summary>
/// One stage's work. Implementations here are deliberately thin stubs — real
/// model calls and workspace-server interaction land in step 3 alongside
/// the front MCP surface (docs/adr/0009-factory-owns-model-access.md). What
/// step 2 needs to prove is the engine's mechanics (checkpointing, the
/// outbox, leasing, retry) around whatever a stage handler returns.
/// </summary>
public interface IStageHandler
{
    StageId Stage { get; }

    Task<StageOutcome> ExecuteAsync(StageContext context, CancellationToken cancellationToken);
}
