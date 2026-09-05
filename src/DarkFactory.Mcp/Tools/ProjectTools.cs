using System.ComponentModel;
using DarkFactory.Core;
using DarkFactory.Data;
using ModelContextProtocol.Server;

namespace DarkFactory.Mcp.Tools;

/// <summary>
/// Projects and runs. <c>work.submit(input: string)</c> is deliberately
/// absent: a run is created from approved amendments now, never from raw
/// text (docs/adr/0003, as amended).
/// </summary>
[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "df.projects.register"),
     Description("Register a project against a workspace MCP server. Runs the handshake and conformance check, derives a name, and seeds the project's default agent team.")]
    public static async Task<ProjectSummary> Register(
        ProjectService projects,
        IConfiguration configuration,
        [Description("The workspace server's MCP endpoint.")] string workspace_mcp_url,
        [Description("Override the derived project name.")] string? name = null,
        CancellationToken cancellationToken = default)
    {
        var registration = await Errors.Surfacing(() => projects.RegisterAsync(
            ServerTools.ResolveOrgId(configuration), workspace_mcp_url, name, cancellationToken));

        return ProjectSummary.From(registration);
    }

    [McpServerTool(Name = "df.projects.list"), Description("List the org's projects.")]
    public static async Task<IReadOnlyList<ProjectSummary>> List(
        ProjectService projects,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var all = await projects.ListAsync(ServerTools.ResolveOrgId(configuration), cancellationToken);
        return all.Select(ProjectSummary.From).ToList();
    }

    [McpServerTool(Name = "df.projects.team"),
     Description("Show which agent handles which stage for a project, and the Foundry deployment each one names.")]
    public static async Task<IReadOnlyList<TeamMemberSummary>> Team(
        TeamService teams,
        [Description("The project.")] string project_id,
        CancellationToken cancellationToken = default)
    {
        var members = await teams.ListAsync(project_id, cancellationToken);
        return members
            .Select(m => new TeamMemberSummary(m.Role, m.Deployment, m.FallbackDeployment, m.TokenBudget))
            .ToList();
    }

    [McpServerTool(Name = "df.work.create"),
     Description("Create a run from approved amendments, against a fresh snapshot of the project's specifications.")]
    public static async Task<RunSummary> CreateWork(
        WorkService work,
        IConfiguration configuration,
        [Description("The project.")] string project_id,
        [Description("Approved amendment ids.")] string[] amendment_ids,
        CancellationToken cancellationToken = default)
    {
        var run = await Errors.Surfacing(() => work.CreateAsync(
            project_id, amendment_ids, ServerTools.ResolveOrgId(configuration), cancellationToken));

        return new RunSummary(
            run.Id, run.ProjectId, run.CurrentStage.ToString(), run.Status.ToString(),
            run.SnapshotId, run.AmendmentIds ?? []);
    }
}

public sealed record ProjectSummary(
    string Id, string Name, string WorkspaceMcpUrl, string[] StackHints, string? TeamId)
{
    public static ProjectSummary From(Project p) =>
        new(p.Id, p.Name, p.WorkspaceMcpUrl, p.StackHints, null);

    public static ProjectSummary From(ProjectRegistration r) =>
        new(r.Project.Id, r.Project.Name, r.Project.WorkspaceMcpUrl, r.Project.StackHints, r.Team.Id);
}

public sealed record TeamMemberSummary(string Role, string Deployment, string? Fallback, int? TokenBudget);

public sealed record RunSummary(
    string Id, string ProjectId, string Stage, string Status, string? SnapshotId, string[] AmendmentIds);
