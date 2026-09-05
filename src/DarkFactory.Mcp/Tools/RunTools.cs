using System.ComponentModel;
using DarkFactory.Data;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// docs/adr/0015: runs are observable and steerable by any authorized
/// client — the dashboard is one consumer of this surface, not the only
/// one, which is why it exists as tools rather than as UI plumbing.
///
/// Both are the stub shape the ADR describes: <c>attach</c> returns the
/// event tail rather than a live stream, and <c>steer</c> records guidance
/// for the next stage rather than interrupting the current one. The stream
/// arrives with the UI in step 4; what matters now is that the shape is
/// fixed before anything is built against it.
/// </summary>
[McpServerToolType]
public static class RunTools
{
    [McpServerTool(Name = "df.work.attach"),
     Description("Observe a run: its stage, status, snapshot, tokens used, and its recent events.")]
    public static async Task<RunAttachment> Attach(
        RunObservationService runs,
        [Description("The run to observe.")] string run_id,
        CancellationToken cancellationToken = default)
    {
        var view = await Errors.Surfacing(() => runs.AttachAsync(run_id, cancellationToken));

        return new RunAttachment(
            view.RunId, view.ProjectId, view.Stage, view.Status, view.SnapshotId, view.TokensUsed,
            view.Events.Select(e => new RunEventSummary(e.Id, e.Type, e.DataJson, e.CreatedAt)).ToList());
    }

    [McpServerTool(Name = "df.work.steer"),
     Description("Inject guidance into the run's next agent turn without cancelling it. Recorded as an event and carried into the next stage's context.")]
    public static async Task<RunEventSummary> Steer(
        RunObservationService runs,
        IConfiguration configuration,
        [Description("The run to steer.")] string run_id,
        [Description("Guidance for the next agent turn.")] string message,
        CancellationToken cancellationToken = default)
    {
        var recorded = await Errors.Surfacing(() => runs.SteerAsync(
            run_id, message, ServerTools.ResolveOrgId(configuration), cancellationToken));

        return new RunEventSummary(recorded.Id, recorded.Type, recorded.DataJson, recorded.CreatedAt);
    }
}

public sealed record RunEventSummary(string Id, string Type, string? Data, DateTimeOffset CreatedAt);

public sealed record RunAttachment(
    string RunId,
    string ProjectId,
    string Stage,
    string Status,
    string? SnapshotId,
    int TokensUsed,
    IReadOnlyList<RunEventSummary> Events);
