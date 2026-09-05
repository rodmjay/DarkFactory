using System.Text.Json;
using DarkFactory.Core;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>A team member resolved for one assignment point, ready to be called.</summary>
public sealed record ResolvedAgent(
    string TeamId,
    string TeamMemberId,
    string Role,
    string Deployment,
    string? FallbackDeployment,
    int? TokenBudget,
    int? MaxOutputTokens);

public sealed class TeamNotConfiguredException(string message) : Exception(message);

/// <summary>
/// The project's standing team (docs/adr/0028): who does what, and which
/// Foundry deployment each of them names.
///
/// Note that nothing here knows what model a deployment resolves to —
/// that's the gateway's business (docs/adr/0027). This layer decides
/// *which role* handles a piece of work, which is docs/adr/0022's
/// verifiability question, and it decides it from configuration rather than
/// from code.
/// </summary>
public sealed class TeamService(DarkFactoryDbContext db)
{
    /// <summary>
    /// The built-in template every new project starts with
    /// (docs/adr/0022's default policy). Deployment names match role names:
    /// docs/adr/0027 names Foundry deployments by role precisely so this
    /// mapping is the identity until someone changes it.
    /// </summary>
    public static IReadOnlyList<TeamTemplateMember> DefaultTemplate { get; } =
    [
        // Spec amendment goes to the strongest model regardless of cost:
        // its failures are subtle and nothing downstream would catch them
        // (docs/adr/0022).
        new(AgentRoles.Architect, AssignmentPoints.Conversation,
            ["df.specs.query", "df.specs.get", "df.specs.neighborhood", "df.specs.propose"]),
        new(AgentRoles.Planner, AssignmentPoints.Plan,
            ["df.specs.get", "df.files.list", "df.files.read_many"]),
        // Whole files, so a chat-sized output limit truncates it mid-JSON.
        new(AgentRoles.Implementer, AssignmentPoints.Implement,
            ["df.files.read_many", "df.files.write_many", "df.exec.run"],
            MaxOutputTokens: ImplementerMaxOutputTokens),
        // Verification is checkable by a test, so it does not need the
        // strongest model — but it does need to be able to run one.
        new(AgentRoles.Reviewer, AssignmentPoints.Verify,
            ["df.files.read_many", "df.exec.run"]),
        new(AgentRoles.Router, AssignmentPoints.Triage, []),
    ];

    public sealed record TeamTemplateMember(
        string Role, string Point, IReadOnlyList<string> Capabilities, int? MaxOutputTokens = null);

    /// <summary>
    /// What an implementer needs to return whole files without being cut
    /// off mid-answer. Held as a named constant rather than a literal in
    /// the template because the failure it prevents — truncation that
    /// presents as a malformed response — is not obvious from the number.
    /// </summary>
    public const int ImplementerMaxOutputTokens = 32_000;

    /// <summary>
    /// Seeds the default team. Called at project registration so a project
    /// is never in the state of existing but having nobody assigned to do
    /// anything — a conversation that failed with "no team configured"
    /// would be a setup step we forgot to make automatic.
    /// </summary>
    public async Task<Team> SeedDefaultTeamAsync(
        string projectId, string orgId, string? testCommand = null, CancellationToken cancellationToken = default)
    {
        var existing = await db.Teams.SingleOrDefaultAsync(
            t => t.ProjectId == projectId && t.IsActive, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var team = new Team
        {
            Id = Ulid.NewUlid(),
            ProjectId = projectId,
            OrgId = orgId,
            Name = "default",
            IsActive = true,
            TestCommand = testCommand,
            CreatedAt = now,
        };
        db.Teams.Add(team);

        foreach (var template in DefaultTemplate)
        {
            var member = new TeamMember
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Role = template.Role,
                Deployment = template.Role,
                FallbackDeployment = null,
                TokenBudget = null,
                MaxOutputTokens = template.MaxOutputTokens,
                CapabilitiesJson = JsonSerializer.Serialize(template.Capabilities),
                CreatedAt = now,
            };
            db.TeamMembers.Add(member);

            db.Assignments.Add(new Assignment
            {
                Id = Ulid.NewUlid(),
                TeamId = team.Id,
                Point = template.Point,
                TeamMemberId = member.Id,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return team;
    }

    /// <summary>
    /// Resolves who handles an assignment point for a project. Throws
    /// rather than falling back to a default deployment: a silent fallback
    /// would mean a project whose team was misconfigured still produced
    /// plausible output, from a model nobody chose.
    /// </summary>
    public async Task<ResolvedAgent> ResolveAsync(
        string projectId, string point, CancellationToken cancellationToken = default)
    {
        var team = await db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.ProjectId == projectId && t.IsActive, cancellationToken)
            ?? throw new TeamNotConfiguredException(
                $"Project '{projectId}' has no active team. A team is seeded at project registration " +
                "(docs/adr/0028); this project predates that or its team was deactivated.");

        var resolved = await (
            from a in db.Assignments.AsNoTracking()
            join m in db.TeamMembers.AsNoTracking() on a.TeamMemberId equals m.Id
            where a.TeamId == team.Id && a.Point == point
            select new ResolvedAgent(team.Id, m.Id, m.Role, m.Deployment, m.FallbackDeployment, m.TokenBudget, m.MaxOutputTokens))
            .SingleOrDefaultAsync(cancellationToken);

        return resolved ?? throw new TeamNotConfiguredException(
            $"Team '{team.Name}' for project '{projectId}' has nobody assigned to '{point}'.");
    }

    public async Task<IReadOnlyList<ResolvedAgent>> ListAsync(
        string projectId, CancellationToken cancellationToken = default)
    {
        var team = await db.Teams.AsNoTracking()
            .SingleOrDefaultAsync(t => t.ProjectId == projectId && t.IsActive, cancellationToken);
        if (team is null)
        {
            return [];
        }

        return await (
            from a in db.Assignments.AsNoTracking()
            join m in db.TeamMembers.AsNoTracking() on a.TeamMemberId equals m.Id
            where a.TeamId == team.Id
            orderby a.Point
            select new ResolvedAgent(team.Id, m.Id, m.Role, m.Deployment, m.FallbackDeployment, m.TokenBudget, m.MaxOutputTokens))
            .ToListAsync(cancellationToken);
    }
}
